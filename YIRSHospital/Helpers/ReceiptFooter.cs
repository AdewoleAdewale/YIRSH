using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xamarin.Forms;

namespace YIRSH.Helpers
{
    /// <summary>
    /// Single source of truth for the barcode + watermark shown under EVERY receipt.
    /// Use BuildView() for on-screen receipts and BuildEscPos() for thermal printing.
    /// </summary>
    public static class ReceiptFooter
    {
        // ADAPT: change wording to whatever the watermark should say.
        public const string WatermarkLine1 = "Powered by OSOFTPAY";
        public const string WatermarkLine2 = "Scan barcode to verify this receipt";

        private const int QuietModules = 10;

        // ---------- shared pixel row ----------
        private static bool[] BuildPixelRow(string data, int scale)
        {
            List<bool> modules = Code128.Encode(data);
            int total = (modules.Count + QuietModules * 2) * scale;
            var row = new bool[total];
            for (int m = 0; m < modules.Count; m++)
            {
                if (!modules[m]) continue;
                int x0 = (QuietModules + m) * scale;
                for (int k = 0; k < scale; k++) row[x0 + k] = true;
            }
            return row;
        }

        // ---------- on-screen ----------
        public static byte[] BuildBmp(string data, int scale, int height)
        {
            bool[] row = BuildPixelRow(data, scale);
            int w = row.Length;
            int rowSize = ((w * 3 + 3) / 4) * 4;
            int imageSize = rowSize * height;
            var bytes = new byte[54 + imageSize];

            bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
            WriteInt(bytes, 2, 54 + imageSize);
            WriteInt(bytes, 10, 54);
            WriteInt(bytes, 14, 40);
            WriteInt(bytes, 18, w);
            WriteInt(bytes, 22, height);
            bytes[26] = 1;
            bytes[28] = 24;
            WriteInt(bytes, 34, imageSize);

            for (int y = 0; y < height; y++)
            {
                int offset = 54 + y * rowSize;
                for (int x = 0; x < rowSize; x++) bytes[offset + x] = 0xFF; // white + padding
                for (int x = 0; x < w; x++)
                {
                    if (!row[x]) continue;
                    int p = offset + x * 3;
                    bytes[p] = 0; bytes[p + 1] = 0; bytes[p + 2] = 0;
                }
            }
            return bytes;
        }

        private static void WriteInt(byte[] b, int offset, int value)
        {
            b[offset] = (byte)(value & 0xFF);
            b[offset + 1] = (byte)((value >> 8) & 0xFF);
            b[offset + 2] = (byte)((value >> 16) & 0xFF);
            b[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static ImageSource CreateBarcodeImage(string data, int scale = 2, int height = 70)
        {
            byte[] bmp = BuildBmp(data, scale, height);
            return ImageSource.FromStream(() => new MemoryStream(bmp));
        }

        /// <summary>Drop this at the bottom of any receipt layout.</summary>
        public static View BuildView(string transactionId)
        {
            var stack = new StackLayout
            {
                Spacing = 4,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalOptions = LayoutOptions.Fill
            };

            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                stack.Children.Add(new Image
                {
                    Source = CreateBarcodeImage(transactionId.Trim()),
                    HeightRequest = 70,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center
                });
                stack.Children.Add(new Label
                {
                    Text = transactionId.Trim(),
                    FontSize = 11,
                    HorizontalTextAlignment = TextAlignment.Center,
                    TextColor = Color.Black
                });
            }

            stack.Children.Add(new Label
            {
                Text = WatermarkLine1,
                FontSize = 10,
                Opacity = 0.45,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = Color.Gray
            });
            stack.Children.Add(new Label
            {
                Text = WatermarkLine2,
                FontSize = 9,
                Opacity = 0.45,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = Color.Gray
            });
            return stack;
        }

        // ---------- thermal printer (ESC/POS) ----------
        /// <summary>
        /// Bytes to append after the last line of the receipt body: centered barcode raster,
        /// transaction ID, watermark, feed. Append BEFORE the paper-cut command.
        /// Width is ~310 dots at scale 2 for an 18-digit ID, fits 58mm (384 dots) printers.
        /// </summary>
        public static byte[] BuildEscPos(string transactionId)
        {
            var o = new List<byte>();
            o.AddRange(new byte[] { 0x1B, 0x61, 0x01 }); // center

            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                bool[] row = BuildPixelRow(transactionId.Trim(), 2);
                int height = 60;
                int widthBytes = (row.Length + 7) / 8;
                var line = new byte[widthBytes];
                for (int x = 0; x < row.Length; x++)
                    if (row[x]) line[x / 8] |= (byte)(0x80 >> (x % 8));

                // GS v 0 m xL xH yL yH d...
                o.AddRange(new byte[] { 0x1D, 0x76, 0x30, 0x00,
                    (byte)(widthBytes & 0xFF), (byte)(widthBytes >> 8),
                    (byte)(height & 0xFF), (byte)(height >> 8) });
                for (int y = 0; y < height; y++) o.AddRange(line);

                o.AddRange(Encoding.ASCII.GetBytes(transactionId.Trim() + "\n"));
            }

            o.AddRange(Encoding.ASCII.GetBytes(WatermarkLine1 + "\n"));
            o.AddRange(Encoding.ASCII.GetBytes(WatermarkLine2 + "\n\n\n"));
            o.AddRange(new byte[] { 0x1B, 0x61, 0x00 }); // left align again
            return o.ToArray();
        }
    }
}