using Android.Bluetooth;
using Android.Graphics;
using Java.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;



namespace YIRSHospital.Services
{
    public sealed class BluetoothPrinterService : IDisposable
    {
        #region ── Constants ──────────────────────────────────────

        private const string SPP_UUID = "00001101-0000-1000-8000-00805f9b34fb";

        /// <summary>
        /// Fixed MAC address of the built-in printer on Sunyard S60 and
        /// compatible Android POS terminals.
        /// The device only appears in the paired-device list when the
        /// "Bluetooth Printer" option is enabled in the POS Setup application.
        /// </summary>
        private const string BUILTIN_PRINTER_MAC = "FF:FF:FF:FF:FF:FF";

        private const int DOTS_58MM = 384;
        private const int DOTS_80MM = 576;
        private const int CONNECT_TIMEOUT_MS = 15_000;
        private const int PRINT_TIMEOUT_MS = 45_000;
        private const int CHUNK_SIZE = 512;
        private const int INTER_CHUNK_DELAY = 20;
        private const int FLUSH_SETTLE_MS = 2_500;


        private const int WATERMARK_GRAY = 185;
        private const float WATERMARK_TEXT_SIZE = 52f;
        private const int WATERMARK_HEIGHT = 88;

        #endregion

        #region ── ESC/POS Commands ───────────────────────────────

        private static readonly byte[] CMD_INIT = { 0x1B, 0x40 };
        private static readonly byte[] CMD_ALIGN_LEFT = { 0x1B, 0x61, 0x00 };
        private static readonly byte[] CMD_ALIGN_CENTER = { 0x1B, 0x61, 0x01 };
        private static readonly byte[] CMD_BOLD_ON = { 0x1B, 0x45, 0x01 };
        private static readonly byte[] CMD_BOLD_OFF = { 0x1B, 0x45, 0x00 };
        private static readonly byte[] CMD_DHEIGHT_ON = { 0x1D, 0x21, 0x01 };
        private static readonly byte[] CMD_DHEIGHT_OFF = { 0x1D, 0x21, 0x00 };
        private static readonly byte[] CMD_DWIDTH_ON = { 0x1D, 0x21, 0x10 };
        private static readonly byte[] CMD_UNDERLINE_ON = { 0x1B, 0x2D, 0x01 };
        private static readonly byte[] CMD_UNDERLINE_OFF = { 0x1B, 0x2D, 0x00 };
        private static readonly byte[] CMD_FONT_SMALL = { 0x1B, 0x4D, 0x01 };
        private static readonly byte[] CMD_FONT_NORMAL = { 0x1B, 0x4D, 0x00 };
        private static readonly byte[] CMD_LF = { 0x0A };
        private static readonly byte[] CMD_FEED_CUT = { 0x1B, 0x64, 0x04, 0x1D, 0x56, 0x42, 0x00 };

        // ── Left-margin reset: GS L nL nH  (set left margin to 0) ──
        private static readonly byte[] CMD_MARGIN_RESET = { 0x1D, 0x4C, 0x00, 0x00 };

        #endregion

        #region ── Supported External Printer Names ───────────────

        /// <summary>
        /// Device names of known external Bluetooth thermal printers.
        /// The built-in POS printer is found by MAC address (see BUILTIN_PRINTER_MAC).
        /// </summary>
        private static readonly HashSet<string> SupportedPrinterNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Sunyard / POS device names on some firmware versions
                "S60", "SUNYARD_S60", "InnerPrinter", "Bluetooth Printer",

                // Common external mobile thermal printers
                "RRN2OP",
                "MPT-II", "MTP-II_89EB", "MTP-II-6111",
                "RPP02N", "RPP210",
                "MP300", "IposPrinter", "FP8800",
                "Internal Bluetooth Printer",
                "printer001", "b906", "ANDROID BT", "CS10",
                "Q2i",
            };

        #endregion

        #region ── Fields ─────────────────────────────────────────

        private readonly int _printerDots;
        private readonly int _charsPerLine;
        private bool _disposed;

        /// <param name="use80mm">
        ///   Pass <c>true</c> for 80 mm paper (576 dots).
        ///   Default <c>false</c> = 58 mm / S60 internal paper (384 dots).
        /// </param>
        public BluetoothPrinterService(bool use80mm = false)
        {
            _printerDots = use80mm ? DOTS_80MM : DOTS_58MM;
            _charsPerLine = use80mm ? 48 : 32;
        }

        #endregion

        // ══════════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════════

        public async Task PrintReceiptAsync(
            ReceiptData receipt,
            string logoAssetName = "Logo.png",
            string watermarkText = "YOBE STATE HOSPITAL",
            CancellationToken cancellationToken = default)
        {
            RequireBluetoothPermissions();

            var adapter = GetAdapter();
            var device = FindPrinterDevice(adapter);

            var buffer = await Task.Run(
                () => BuildPrintBuffer(receipt, logoAssetName, watermarkText),
                cancellationToken);

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                linked.CancelAfter(PRINT_TIMEOUT_MS);
                await ConnectAndTransmitAsync(device, buffer, linked.Token);
            }
            finally { linked.Dispose(); }
        }

        public async Task PrintTestPageAsync(CancellationToken cancellationToken = default)
        {
            RequireBluetoothPermissions();

            var adapter = GetAdapter();
            var device = FindPrinterDevice(adapter);

            var buffer = await Task.Run(() => BuildTestBuffer(), cancellationToken);

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                linked.CancelAfter(PRINT_TIMEOUT_MS);
                await ConnectAndTransmitAsync(device, buffer, linked.Token);
            }
            finally { linked.Dispose(); }
        }

        // ══════════════════════════════════════════════════════════
        //  DEVICE DISCOVERY
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Searches paired Bluetooth devices for a printer.
        ///
        /// Priority
        /// ────────
        /// 1. Built-in POS printer  (MAC = FF:FF:FF:FF:FF:FF)
        ///    Appears in paired list only when "Bluetooth Printer" is enabled
        ///    in the POS Setup application.
        ///
        /// 2. External printer matched by well-known device name.
        ///
        /// Throws <see cref="PrinterException"/> when nothing is found.
        /// </summary>
        private static BluetoothDevice FindPrinterDevice(BluetoothAdapter adapter)
        {
            var bonded = adapter.BondedDevices;

            if (bonded == null || !bonded.Any())
                throw new PrinterException(
                    "No paired Bluetooth devices found.\n" +
                    "• Built-in POS printer: enable the Bluetooth Printer option in " +
                    "the POS Setup app, then the device (FF:FF:FF:FF:FF:FF) will " +
                    "appear in Android Bluetooth paired list.\n" +
                    "• External printer: pair it via Android Bluetooth Settings.");

            // ── 1. Built-in printer by fixed MAC ─────────────────────────
            foreach (var d in bonded)
            {
                if (string.Equals(d.Address, BUILTIN_PRINTER_MAC,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Built-in POS printer found → {d.Address}");
                    return d;
                }
            }

            // ── 2. External printer by device name ───────────────────────
            foreach (var d in bonded)
            {
                if (SupportedPrinterNames.Contains(d.Name ?? string.Empty))
                {
                    Log($"External printer found → {d.Name} ({d.Address})");
                    return d;
                }
            }

            throw new PrinterException(
                "No compatible printer found in paired devices.\n\n" +
                "Built-in POS printer\n" +
                "  • Open the POS Setup app.\n" +
                "  • Enable the Bluetooth Printer option.\n" +
                "  • The printer (MAC FF:FF:FF:FF:FF:FF) will appear as a paired device.\n\n" +
                "External Bluetooth printer\n" +
                "  • Switch the printer on.\n" +
                "  • Go to Android Settings → Bluetooth → Pair new device.");
        }

        // ══════════════════════════════════════════════════════════
        //  BLUETOOTH CONNECTION
        // ══════════════════════════════════════════════════════════

        private async Task ConnectAndTransmitAsync(
            BluetoothDevice device, byte[] buffer, CancellationToken token)
        {
            BluetoothSocket socket = null;
            try
            {
                socket = await OpenSocketAsync(device, token);
                if (!socket.IsConnected)
                    throw new PrinterException("Socket open but IsConnected = false.");

                using (var output = socket.OutputStream)
                    await SendChunkedAsync(output, buffer, token);

                await Task.Delay(FLUSH_SETTLE_MS, token);
            }
            finally { SafeDispose(socket); }
        }

        /// <summary>
        /// Three-strategy connection to handle both the built-in POS printer
        /// (which may lack a proper SDP record) and standard external printers.
        ///
        /// Strategy A – SPP UUID          : works for most external printers
        /// Strategy B – Channel-1 RFCOMM  : works for built-in / no-SDP devices
        /// Strategy C – Insecure SPP UUID : last resort for older POS firmware
        /// </summary>
        private async Task<BluetoothSocket> OpenSocketAsync(
            BluetoothDevice device, CancellationToken token)
        {
            Exception lastEx = null;

            // ── A: standard SPP UUID ──────────────────────────────────────
            BluetoothSocket socket = null;
            try
            {
                socket = device.CreateRfcommSocketToServiceRecord(UUID.FromString(SPP_UUID));
                await ConnectWithTimeoutAsync(socket, CONNECT_TIMEOUT_MS, token);
                Log("Connected via SPP UUID.");
                return socket;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                Log($"SPP UUID failed ({ex.Message}); trying channel-1.");
                SafeDispose(socket);
                socket = null;
            }

            // ── B: channel-1 RFCOMM (built-in printer / no SDP) ──────────
            try
            {
                var method = device.Class.GetMethod("createRfcommSocket", Java.Lang.Integer.Type);
                var ch1Socket = (BluetoothSocket)method.Invoke(device, 1);
                await ConnectWithTimeoutAsync(ch1Socket, CONNECT_TIMEOUT_MS, token);
                Log("Connected via channel-1 RFCOMM.");
                return ch1Socket;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                Log($"Channel-1 failed ({ex.Message}); trying insecure UUID.");
            }

            // ── C: insecure RFCOMM (some older POS firmware) ─────────────
            try
            {
                var insecureSocket = device.CreateInsecureRfcommSocketToServiceRecord(
                    UUID.FromString(SPP_UUID));
                await ConnectWithTimeoutAsync(insecureSocket, CONNECT_TIMEOUT_MS, token);
                Log("Connected via insecure SPP UUID.");
                return insecureSocket;
            }
            catch (Exception ex)
            {
                throw new PrinterException(
                    "Could not connect to printer after 3 attempts.\n" +
                    "Last error: " + ex.Message + "\n\n" +
                    "• Built-in printer: confirm the Bluetooth Printer toggle is ON " +
                    "in the POS Setup app.\n" +
                    "• External printer: confirm it is switched on and in range.");
            }
        }

        private static async Task ConnectWithTimeoutAsync(
            BluetoothSocket socket, int timeoutMs, CancellationToken token)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);
            try
            {
                var connectTask = socket.ConnectAsync();
                var completedTask = await Task.WhenAny(
                    connectTask,
                    Task.Delay(System.Threading.Timeout.Infinite, cts.Token));

                if (completedTask != connectTask)
                    throw new PrinterException(
                        "Bluetooth connection timed out. Is the printer on?");
                await connectTask;
            }
            catch (OperationCanceledException)
            {
                throw new PrinterException(
                    "Bluetooth connection timed out. Is the printer on?");
            }
            finally { cts.Dispose(); }
        }

        private static async Task SendChunkedAsync(
            Stream output, byte[] data, CancellationToken token)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(CHUNK_SIZE, data.Length - offset);
                await output.WriteAsync(data, offset, count, token);
                await output.FlushAsync(token);
                offset += count;
                if (INTER_CHUNK_DELAY > 0)
                    await Task.Delay(INTER_CHUNK_DELAY, token);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  TEST PAGE BUFFER
        // ══════════════════════════════════════════════════════════

        private byte[] BuildTestBuffer()
        {
            var ms = new MemoryStream(2048);
            try
            {
                ms.Write(CMD_INIT);
                ms.Write(CMD_ALIGN_CENTER);
                ms.WriteText(Divider('=', _charsPerLine) + "\n");

                ms.Write(CMD_ALIGN_LEFT);
                ms.Write(CMD_LF);
                ms.Write(CMD_BOLD_ON);
                ms.WriteText("PRINTER STATUS: ONLINE\n");
                ms.Write(CMD_BOLD_OFF);
                ms.WriteText(Divider('-', _charsPerLine) + "\n");


                return ms.ToArray();
            }
            finally { ms.Dispose(); }
        }

        // ══════════════════════════════════════════════════════════
        //  RECEIPT BUFFER
        // ══════════════════════════════════════════════════════════

        private byte[] BuildPrintBuffer(
      ReceiptData receipt,
      string logoAssetName,
      string watermarkText)
        {
            // Pre-build the watermark bytes once; reuse for every section.
            byte[] wm = null;
            if (!string.IsNullOrWhiteSpace(watermarkText))
                wm = BuildWatermarkCommand(watermarkText);

            var ms = new MemoryStream(8192);
            try
            {
                ms.Write(CMD_INIT);

                ms.Write(CMD_ALIGN_CENTER);
                ms.WriteText(Divider('=', _charsPerLine) + "\n");

                if (!string.IsNullOrWhiteSpace(logoAssetName))
                {
                    var logoCmd = TryBuildLogoCommand(logoAssetName, maxWidth: 180);
                    if (logoCmd != null) ms.Write(logoCmd);
                }

                ms.Write(CMD_ALIGN_CENTER);
                ms.Write(CMD_BOLD_ON);
                ms.WriteText(receipt.StoreName + "\n");
                ms.Write(CMD_BOLD_OFF);

                if (!string.IsNullOrWhiteSpace(receipt.StorePhone))
                    ms.WriteText(receipt.StorePhone + "\n");

                ms.WriteText(Divider('=', _charsPerLine) + "\n");

                // ── Receipt banner ───────────────────────────────────────────
                ms.Write(CMD_ALIGN_CENTER);
                ms.Write(CMD_BOLD_ON);
                ms.Write(CMD_DWIDTH_ON);
                ms.WriteText((receipt.ReceiptBannerText ?? "OFFICIAL RECEIPT") + "\n");
                ms.WriteByte(0x1D); ms.WriteByte(0x21); ms.WriteByte(0x00); // reset scale
                ms.Write(CMD_BOLD_OFF);
                ms.WriteText(Divider('=', _charsPerLine) + "\n");


                // ── Metadata ─────────────────────────────────────────────────
                ms.Write(CMD_ALIGN_LEFT);
                ms.WriteText(Col("Date", receipt.PrintDate.ToString("dd/MM/yyyy HH:mm:ss"), _charsPerLine) + "\n");
                ms.WriteText(Col("Ref", receipt.ReceiptNumber, _charsPerLine) + "\n");
                ms.WriteText(Col("Agent", receipt.AgentName, _charsPerLine) + "\n");
                ms.WriteText(Col("Point", receipt.CollectionPoint, _charsPerLine) + "\n");

                if (!string.IsNullOrWhiteSpace(receipt.Consultant))
                    ms.WriteText(Col("Consult", receipt.Consultant, _charsPerLine) + "\n");
                if (!string.IsNullOrWhiteSpace(receipt.SuperAgent))
                    ms.WriteText(Col("S.Agent", receipt.SuperAgent, _charsPerLine) + "\n");

                ms.WriteText(Divider('-', _charsPerLine) + "\n");


                // ── Items ────────────────────────────────────────────────────
                foreach (var item in receipt.Items)
                {
                    if (item.Amount == 0m && !string.IsNullOrWhiteSpace(item.SubText))
                    {
                        // Info-only row: label left-aligned, value right of colon
                        ms.Write(CMD_ALIGN_LEFT);
                        ms.WriteText(Col(item.Description, item.SubText, _charsPerLine) + "\n");
                    }
                    else
                    {
                        ms.Write(CMD_ALIGN_LEFT);
                        ms.WriteText(ColTwoRight(
                            item.Description,
                            "N" + item.Amount.ToString("###,###.00"),
                            _charsPerLine) + "\n");

                        if (!string.IsNullOrWhiteSpace(item.SubText))
                        {
                            ms.Write(CMD_FONT_SMALL);
                            ms.WriteText("  " + item.SubText + "\n");
                            ms.Write(CMD_FONT_NORMAL);
                        }
                    }
                }

                ms.WriteText(Divider('-', _charsPerLine) + "\n");


                // ── Totals ───────────────────────────────────────────────────
                ms.Write(CMD_ALIGN_LEFT);
                ms.Write(CMD_BOLD_ON);

                if (receipt.TotalAmount > 0m)
                    ms.WriteText(ColTwoRight("TOTAL AMOUNT",
                        "N" + receipt.TotalAmount.ToString("###,###.00"), _charsPerLine) + "\n");

                if (receipt.AmountPaid > 0m)
                    ms.WriteText(ColTwoRight("AMOUNT PAID",
                        "N" + receipt.AmountPaid.ToString("###,###.00"), _charsPerLine) + "\n");

                if (receipt.AmountLeft > 0m)
                    ms.WriteText(ColTwoRight("BALANCE DUE",
                        "N" + receipt.AmountLeft.ToString("###,###.00"), _charsPerLine) + "\n");

                ms.Write(CMD_BOLD_OFF);
                ms.WriteText(Divider('=', _charsPerLine) + "\n");


                // ── QR Code (centred) ────────────────────────────────────────
                // CMD_ALIGN_CENTER controls text alignment but does NOT move the
                // QR raster block itself on many thermal firmware builds.
                // We therefore compute a left margin so the QR module sits in the
                // middle of the paper, then reset it to 0 afterwards.

                ms.Write(CMD_ALIGN_CENTER);
                if (!string.IsNullOrWhiteSpace(receipt.BarcodeLabel))
                {
                    ms.Write(CMD_ALIGN_CENTER);
                    ms.Write(CMD_FONT_SMALL);
                    ms.WriteText("SCAN TO VERIFY\n");
                    ms.Write(CMD_FONT_NORMAL);

                    const byte qrCellSize = 3;
                    int estimatedQrDots = EstimateQrDots(receipt.BarcodeLabel, qrCellSize);
                    int qrLeftMargin = Math.Max(0, (_printerDots - estimatedQrDots) / 2);

                    // Set left margin: GS L nL nH
                    ms.Write(SetLeftMargin(qrLeftMargin));
                    ms.Write(BuildQRCodeCommand(receipt.BarcodeLabel, qrCellSize));
                    // Reset left margin to zero so the rest of the receipt is unaffected
                    ms.Write(CMD_MARGIN_RESET);

                    ms.Write(CMD_ALIGN_LEFT);
                    ms.WriteText(Divider('-', _charsPerLine) + "\n");
                }

                ms.Write(CMD_ALIGN_CENTER);
                ms.Write(CMD_LF);
                ms.Write(CMD_BOLD_ON);
                ms.WriteText((receipt.FooterLine2 ?? "POWERED BY OSOFTPAY") + "\n");
                ms.Write(CMD_BOLD_OFF);
                ms.WriteText(Divider('=', _charsPerLine) + "\n");

                ms.Write(CMD_ALIGN_LEFT);
                ms.Write(CMD_LF);
                ms.Write(CMD_LF);
                ms.Write(CMD_FEED_CUT);

                return ms.ToArray();
            }
            finally { ms.Dispose(); }
        }

        // ══════════════════════════════════════════════════════════
        //  WATERMARK
        // ══════════════════════════════════════════════════════════

        private byte[] BuildWatermarkCommand(string text)
        {
            try
            {
                int width = (_printerDots / 8) * 8;
                int height = WATERMARK_HEIGHT;

                Bitmap bmp = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888);
                Canvas canvas = new Canvas(bmp);
                canvas.DrawColor(Color.White);

                Paint paint = new Paint { AntiAlias = true };
                paint.TextSize = WATERMARK_TEXT_SIZE;
                paint.SetARGB(255, WATERMARK_GRAY, WATERMARK_GRAY, WATERMARK_GRAY);

                float tw = paint.MeasureText(text);
                float x = (width - tw) / 2f;
                float y = height * 0.70f;

                canvas.Save();
                canvas.Rotate(-14f, width / 2f, height / 2f);
                canvas.DrawText(text, x, y, paint);
                canvas.Restore();

                int widthBytes = width / 8;
                byte[] raster = ConvertToMonochromeFloydSteinberg(bmp, width, height);

                bmp.Recycle();
                paint.Dispose();

                var ms = new MemoryStream();
                try
                {
                    byte xL = (byte)(widthBytes & 0xFF);
                    byte xH = (byte)((widthBytes >> 8) & 0xFF);
                    byte yL = (byte)(height & 0xFF);
                    byte yH = (byte)((height >> 8) & 0xFF);
                    ms.Write(new byte[] { 0x1D, 0x76, 0x30, 0x00, xL, xH, yL, yH });
                    ms.Write(raster, 0, raster.Length);
                    ms.Write(CMD_LF);
                    return ms.ToArray();
                }
                finally { ms.Dispose(); }
            }
            catch (Exception ex)
            {
                Log($"Watermark skipped – {ex.Message}");
                return null;
            }
        }



        // ══════════════════════════════════════════════════════════
        //  QR CODE  (ESC/POS Model-2)
        // ══════════════════════════════════════════════════════════

        private static byte[] BuildQRCodeCommand(
            string data,
            byte cellSize = 3,
            byte errorLevel = 77)
        {
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            int storeLen = dataBytes.Length + 3;
            byte pL = (byte)(storeLen & 0xFF);
            byte pH = (byte)((storeLen >> 8) & 0xFF);

            var ms = new MemoryStream();
            try
            {
                ms.Write(new byte[] { 0x1D, 0x28, 0x6B, 4, 0, 49, 65, 50, 0 });
                ms.Write(new byte[] { 0x1D, 0x28, 0x6B, 3, 0, 49, 67, cellSize });
                ms.Write(new byte[] { 0x1D, 0x28, 0x6B, 3, 0, 49, 69, errorLevel });
                ms.Write(new byte[] { 0x1D, 0x28, 0x6B, pL, pH, 49, 80, 48 });
                ms.Write(dataBytes, 0, dataBytes.Length);
                ms.Write(new byte[] { 0x1D, 0x28, 0x6B, 3, 0, 49, 81, 48 });
                ms.WriteByte(0x0A);
                return ms.ToArray();
            }
            finally { ms.Dispose(); }
        }

        // ══════════════════════════════════════════════════════════
        //  LOGO
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Loads <paramref name="assetName"/> from the app's Assets folder,
        /// scales it to fit the paper, converts it to 1-bit raster, then wraps
        /// it in a GS v 0 command preceded by a GS L (left-margin) command that
        /// physically centres the image on the paper.  The margin is reset to 0
        /// after the image data so subsequent text is not indented.
        /// </summary>
        private byte[] TryBuildLogoCommand(string assetName, int maxWidth = 180)
        {
            try
            {
                var context = Android.App.Application.Context;
                Bitmap src;
                using (var stream = context.Assets.Open(assetName))
                    src = BitmapFactory.DecodeStream(stream);

                if (src == null) { Log($"Asset '{assetName}' not decoded."); return null; }

                var scaled = ScaleToFitPaper(src, maxWidth);
                src.Recycle();

                int widthDots = scaled.Width;
                int heightDots = scaled.Height;
                int widthBytes = widthDots / 8;

                byte[] raster = ConvertToMonochromeThreshold(scaled, widthDots, heightDots);
                scaled.Recycle();

                // Compute the left margin required to centre the image.
                // _printerDots is the full printable width in dots.
                int leftMarginDots = Math.Max(0, (_printerDots - widthDots) / 2);

                var ms = new MemoryStream();
                try
                {
                    // Set left margin so the raster block is centred
                    ms.Write(SetLeftMargin(leftMarginDots));

                    byte xL = (byte)(widthBytes & 0xFF);
                    byte xH = (byte)((widthBytes >> 8) & 0xFF);
                    byte yL = (byte)(heightDots & 0xFF);
                    byte yH = (byte)((heightDots >> 8) & 0xFF);
                    ms.Write(new byte[] { 0x1D, 0x76, 0x30, 0x00, xL, xH, yL, yH });
                    ms.Write(raster, 0, raster.Length);

                    // Reset left margin to 0 so normal text is not affected
                    ms.Write(CMD_MARGIN_RESET);
                    ms.Write(CMD_LF);
                    return ms.ToArray();
                }
                finally { ms.Dispose(); }
            }
            catch (Exception ex) { Log($"Logo skipped – {ex.Message}"); return null; }
        }

        // ══════════════════════════════════════════════════════════
        //  IMAGE → MONOCHROME
        // ══════════════════════════════════════════════════════════

        private static Bitmap ScaleToFitPaper(Bitmap source, int maxWidth)
        {
            int targetW = (Math.Min(source.Width, maxWidth) / 8) * 8;
            if (targetW == source.Width && source.Width % 8 == 0) return source;
            float scale = (float)targetW / source.Width;
            int targetH = Math.Max(1, (int)(source.Height * scale));
            return Bitmap.CreateScaledBitmap(source, targetW, targetH, true);
        }

        /// <summary>Simple threshold – best for logos with sharp lines.</summary>
        private static byte[] ConvertToMonochromeThreshold(Bitmap bmp, int w, int h)
        {
            int bpr = w / 8;
            byte[] result = new byte[bpr * h];
            var pixels = new int[w * h];
            bmp.GetPixels(pixels, 0, w, 0, 0, w, h);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int px = pixels[y * w + x];
                    if (((px >> 24) & 0xFF) < 128) continue;
                    int r = (px >> 16) & 0xFF;
                    int g = (px >> 8) & 0xFF;
                    int b = px & 0xFF;
                    if (0.299f * r + 0.587f * g + 0.114f * b < 128f)
                        result[y * bpr + x / 8] |= (byte)(1 << (7 - x % 8));
                }
            return result;
        }

        /// <summary>
        /// Floyd-Steinberg dithering – produces halftone-like output.
        /// Used for the watermark strip.
        /// Standard weights: right 7/16, below-left 3/16, below 5/16, below-right 1/16.
        /// </summary>
        private static byte[] ConvertToMonochromeFloydSteinberg(Bitmap bmp, int w, int h)
        {
            int bpr = w / 8;
            byte[] result = new byte[bpr * h];
            var pixels = new int[w * h];
            bmp.GetPixels(pixels, 0, w, 0, 0, w, h);

            float[] gray = new float[w * h];
            for (int i = 0; i < w * h; i++)
            {
                int px = pixels[i];
                int a = (px >> 24) & 0xFF;
                int r = (px >> 16) & 0xFF;
                int g = (px >> 8) & 0xFF;
                int bl = px & 0xFF;
                gray[i] = a < 128 ? 255f : (0.299f * r + 0.587f * g + 0.114f * bl);
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float old = gray[y * w + x];
                    float neo = old < 128f ? 0f : 255f;
                    gray[y * w + x] = neo;
                    float err = old - neo;

                    if (x + 1 < w) gray[y * w + x + 1] += err * 7f / 16f;
                    if (x > 0 && y + 1 < h) gray[(y + 1) * w + x - 1] += err * 3f / 16f;
                    if (y + 1 < h) gray[(y + 1) * w + x] += err * 5f / 16f;
                    if (x + 1 < w && y + 1 < h) gray[(y + 1) * w + x + 1] += err * 1f / 16f;

                    if (neo < 128f)
                        result[y * bpr + x / 8] |= (byte)(1 << (7 - x % 8));
                }
            }
            return result;
        }

        // ══════════════════════════════════════════════════════════
        //  CENTERING HELPERS
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a GS L (set left margin) command for the given dot offset.
        /// ESC/POS spec: GS L nL nH  where margin = nL + nH*256 dots.
        /// </summary>
        private static byte[] SetLeftMargin(int dots)
        {
            byte nL = (byte)(dots & 0xFF);
            byte nH = (byte)((dots >> 8) & 0xFF);
            return new byte[] { 0x1D, 0x4C, nL, nH };
        }

        /// <summary>
        /// Estimates the printed width (in dots) of a QR code for the given
        /// payload length and cell size.
        ///
        /// QR version is selected by data capacity.  We use the byte-mode
        /// capacities for error-correction level M (errorLevel = 77 = 'M').
        /// This is an approximation; the actual symbol may be 1–2 versions
        /// larger depending on encoding overhead, but the estimate is accurate
        /// enough to centre the image visually.
        ///
        ///  version 1 →  21 modules   ≤  14 bytes
        ///  version 2 →  25 modules   ≤  26 bytes
        ///  version 3 →  29 modules   ≤  42 bytes
        ///  version 4 →  33 modules   ≤  62 bytes
        ///  version 5 →  37 modules   ≤  84 bytes
        ///  version 6 →  41 modules   ≤ 106 bytes
        ///  version 7 →  45 modules   ≤ 122 bytes
        ///  version 8 →  49 modules   ≤ 152 bytes
        ///  version 9 →  53 modules   ≤ 180 bytes
        ///  version 10→  57 modules   ≤ 213 bytes
        /// </summary>
        private static int EstimateQrDots(string data, byte cellSize)
        {
            int byteLen = Encoding.UTF8.GetByteCount(data);

            // (maxBytes, modules) pairs ordered by version
            (int maxBytes, int modules)[] versions =
            {
                (14,  21), (26,  25), (42,  29), (62,  33),
                (84,  37), (106, 41), (122, 45), (152, 49),
                (180, 53), (213, 57),
            };

            int modules = 57; // fallback: version 10
            foreach (var (maxBytes, mod) in versions)
            {
                if (byteLen <= maxBytes) { modules = mod; break; }
            }

            // Each module is cellSize dots; add 2 × quiet-zone (4 modules each side)
            // The quiet zone is already encoded inside the symbol for ESC/POS printers,
            // so we only need to account for the symbol width itself.
            return modules * cellSize;
        }

        // ══════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════

        private static BluetoothAdapter GetAdapter()
        {
            var adapter = BluetoothAdapter.DefaultAdapter
                ?? throw new PrinterException("No Bluetooth adapter on this device.");
            if (!adapter.IsEnabled)
                throw new PrinterException("Bluetooth is off. Enable it and retry.");
            return adapter;
        }

        private static void RequireBluetoothPermissions()
        {
            bool granted = BluetoothPermissionHelper.Request();
            if (!granted)
                throw new PrinterException(
                    "Bluetooth permission denied.\n" +
                    "Android 12+: Settings → Permissions → Nearby devices → Allow.\n" +
                    "Older Android: Allow Location permission.");
        }

        private static string Col(string label, string value, int width)
        {
            string full = label.PadRight(8) + ": " + value;
            return full.Length <= width ? full : full.Substring(0, width);
        }

        private static string ColTwoRight(string left, string right, int width)
        {
            int rw = right.Length;
            int lw = Math.Max(1, width - rw);
            if (left.Length > lw - 1)
                left = left.Substring(0, Math.Max(0, lw - 3)) + "..";
            return left.PadRight(lw) + right;
        }

        private static string Divider(char ch, int len) => new string(ch, len);

        private static void SafeDispose(IDisposable obj) { try { obj?.Dispose(); } catch { } }

        private static void Log(string msg)
            => System.Diagnostics.Debug.WriteLine("[BluetoothPrinterService] " + msg);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  STREAM EXTENSIONS
    // ══════════════════════════════════════════════════════════════

    internal static class StreamExtensions
    {
        private static readonly Encoding PrintEncoding = Encoding.UTF8;

        public static void Write(this MemoryStream ms, byte[] data)
            => ms.Write(data, 0, data.Length);

        public static void WriteText(this MemoryStream ms, string text)
        {
            byte[] bytes = PrintEncoding.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  DATA MODELS
    // ══════════════════════════════════════════════════════════════

    public sealed class ReceiptData
    {
        public string StoreName { get; set; } = "YOBE STATE HOSPITAL";
        public string StoreSubTitle { get; set; }
        public string StoreAddress { get; set; } = "Yobe State, Nigeria";
        public string StorePhone { get; set; } = "Contact: +234 907 070 1616";
        public string ReceiptBannerText { get; set; } = "OFFICIAL RECEIPT";
        public string ReceiptNumber { get; set; } = "N/A";
        public string AgentName { get; set; }
        public string CollectionPoint { get; set; }
        public string Consultant { get; set; }
        public string SuperAgent { get; set; }
        public DateTime PrintDate { get; set; } = DateTime.Now;

        public List<ReceiptItem> Items { get; set; } = new List<ReceiptItem>();

        public decimal TotalAmount { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal AmountLeft { get; set; }

        public string FooterLine1 { get; set; } = "Thank You!";
        public string FooterLine2 { get; set; } = "POWERED BY OSOFTPAY";

        /// <summary>
        /// Full URL encoded as QR code on the receipt.
        /// Set to null to skip the QR section entirely.
        /// </summary>
        public string BarcodeLabel { get; set; }
    }

    public sealed class ReceiptItem
    {
        public string Description { get; set; }
        public decimal Amount { get; set; }
        /// <summary>
        /// When Amount == 0 and SubText is set, the row renders as a
        /// left-aligned info-only line (label: value) with no currency column.
        /// </summary>
        public string SubText { get; set; }
    }


    public sealed class PrinterException : Exception
    {
        public PrinterException(string message) : base(message) { }
        public PrinterException(string message, Exception inner) : base(message, inner) { }
    }
}