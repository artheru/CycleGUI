using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Drawing.Imaging;
using System.Drawing;
using System.Reflection;

namespace CycleGUI
{
	public sealed class SoftwareBitmap
	{
		public int Width { get; }
		public int Height { get; }
		public byte[] Pixels { get; } // RGBA, row-major, origin at top-left


        public enum ColorMapType
        {
            Jet,
            HSV,
            Hot,
            Cool,
            Gray,
            Viridis,
            Plasma
        }

        /// <summary>
        /// Apply a colormap to a single-channel monochrome image
        /// </summary>
        /// <param name="monoData">Single-channel byte array (grayscale values 0-255)</param>
        /// <param name="width">Image width</param>
        /// <param name="height">Image height</param>
        /// <param name="colorMap">Colormap to apply</param>
        /// <returns>ARGB byte array (4 bytes per pixel)</returns>
        public static byte[] ColorMap(byte[] monoData, int width, int height, ColorMapType colorMap = ColorMapType.Jet)
        {
            if (monoData == null || monoData.Length != width * height)
                throw new ArgumentException("monoData must have width * height elements");

            byte[] argbData = new byte[width * height * 4];

            for (int i = 0; i < monoData.Length; i++)
            {
                float value = monoData[i] / 255f; // Normalize to 0-1
                (byte r, byte g, byte b) = ApplyColorMap(value, colorMap);

                int argbIndex = i * 4;
                argbData[argbIndex + 0] = r;     // R
                argbData[argbIndex + 1] = g;     // G
                argbData[argbIndex + 2] = b;     // B
                argbData[argbIndex + 3] = 255;   // A
            }

            return argbData;
        }

        private static (byte r, byte g, byte b) ApplyColorMap(float value, ColorMapType colorMap)
        {
            // Clamp value to 0-1
            value = Math.Max(0, Math.Min(1, value));

            return colorMap switch
            {
                ColorMapType.Jet => JetColorMap(value),
                ColorMapType.HSV => HSVColorMap(value),
                ColorMapType.Hot => HotColorMap(value),
                ColorMapType.Cool => CoolColorMap(value),
                ColorMapType.Gray => GrayColorMap(value),
                ColorMapType.Viridis => ViridisColorMap(value),
                ColorMapType.Plasma => PlasmaColorMap(value),
                _ => JetColorMap(value)
            };
        }

        private static (byte r, byte g, byte b) JetColorMap(float value)
        {
            // Classic Jet colormap: blue -> cyan -> green -> yellow -> red
            float r, g, b;

            if (value < 0.125f)
            {
                r = 0;
                g = 0;
                b = 0.5f + value * 4;
            }
            else if (value < 0.375f)
            {
                r = 0;
                g = (value - 0.125f) * 4;
                b = 1;
            }
            else if (value < 0.625f)
            {
                r = (value - 0.375f) * 4;
                g = 1;
                b = 1 - (value - 0.375f) * 4;
            }
            else if (value < 0.875f)
            {
                r = 1;
                g = 1 - (value - 0.625f) * 4;
                b = 0;
            }
            else
            {
                r = 1 - (value - 0.875f) * 4;
                g = 0;
                b = 0;
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static (byte r, byte g, byte b) HSVColorMap(float value)
        {
            // HSV colormap: varies hue from 0 to 300 degrees (red to magenta)
            float hue = value * 300f / 360f;
            HSVtoRGB(hue, 1f, 1f, out float r, out float g, out float b);
            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static (byte r, byte g, byte b) HotColorMap(float value)
        {
            // Hot colormap: black -> red -> orange -> yellow -> white
            float r, g, b;

            if (value < 0.33f)
            {
                r = value / 0.33f;
                g = 0;
                b = 0;
            }
            else if (value < 0.66f)
            {
                r = 1;
                g = (value - 0.33f) / 0.33f;
                b = 0;
            }
            else
            {
                r = 1;
                g = 1;
                b = (value - 0.66f) / 0.34f;
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static (byte r, byte g, byte b) CoolColorMap(float value)
        {
            // Cool colormap: cyan to magenta
            float r = value;
            float g = 1 - value;
            float b = 1;
            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static (byte r, byte g, byte b) GrayColorMap(float value)
        {
            // Grayscale
            byte gray = (byte)(value * 255);
            return (gray, gray, gray);
        }

        private static (byte r, byte g, byte b) ViridisColorMap(float value)
        {
            // Viridis colormap approximation (perceptually uniform)
            float r, g, b;

            // Simplified Viridis approximation
            if (value < 0.25f)
            {
                float t = value / 0.25f;
                r = 0.267f * (1 - t) + 0.282f * t;
                g = 0.005f * (1 - t) + 0.141f * t;
                b = 0.329f * (1 - t) + 0.490f * t;
            }
            else if (value < 0.5f)
            {
                float t = (value - 0.25f) / 0.25f;
                r = 0.282f * (1 - t) + 0.253f * t;
                g = 0.141f * (1 - t) + 0.265f * t;
                b = 0.490f * (1 - t) + 0.530f * t;
            }
            else if (value < 0.75f)
            {
                float t = (value - 0.5f) / 0.25f;
                r = 0.253f * (1 - t) + 0.478f * t;
                g = 0.265f * (1 - t) + 0.478f * t;
                b = 0.530f * (1 - t) + 0.408f * t;
            }
            else
            {
                float t = (value - 0.75f) / 0.25f;
                r = 0.478f * (1 - t) + 0.993f * t;
                g = 0.478f * (1 - t) + 0.906f * t;
                b = 0.408f * (1 - t) + 0.144f * t;
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static (byte r, byte g, byte b) PlasmaColorMap(float value)
        {
            // Plasma colormap approximation (perceptually uniform)
            float r, g, b;

            // Simplified Plasma approximation
            if (value < 0.33f)
            {
                float t = value / 0.33f;
                r = 0.050f * (1 - t) + 0.658f * t;
                g = 0.030f * (1 - t) + 0.085f * t;
                b = 0.528f * (1 - t) + 0.425f * t;
            }
            else if (value < 0.66f)
            {
                float t = (value - 0.33f) / 0.33f;
                r = 0.658f * (1 - t) + 0.949f * t;
                g = 0.085f * (1 - t) + 0.380f * t;
                b = 0.425f * (1 - t) + 0.145f * t;
            }
            else
            {
                float t = (value - 0.66f) / 0.34f;
                r = 0.949f * (1 - t) + 0.940f * t;
                g = 0.380f * (1 - t) + 0.975f * t;
                b = 0.145f * (1 - t) + 0.131f * t;
            }

            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static void HSVtoRGB(float h, float s, float v, out float r, out float g, out float b)
        {
            int i = (int)(h * 6);
            float f = h * 6 - i;
            float p = v * (1 - s);
            float q = v * (1 - f * s);
            float t = v * (1 - (1 - f) * s);

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        public SoftwareBitmap(int width, int height, uint clearRgba = 0xFF000000)
		{
			if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
			Width = width;
			Height = height;
			Pixels = new byte[width * height * 4];
			Clear(clearRgba);
		}

        public SoftwareBitmap(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException();
            if (rgba.Length != width * height * 4) throw new ArgumentException("RGBA length mismatch");
            Width = width;
            Height = height;
            Pixels = rgba;
        }

        public unsafe void Clear(uint rgba)
		{
			int total = Width * Height;
			fixed (byte* pBase = Pixels)
			{
				uint* p = (uint*)pBase;
				for (int i = 0; i < total; i++)
				{
					p[i] = rgba;
				}
			}
		}

		public uint GetPixel(int x, int y)
		{
			if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
			int i = (y * Width + x) * 4;
			byte r = Pixels[i + 0];
			byte g = Pixels[i + 1];
			byte b = Pixels[i + 2];
			byte a = Pixels[i + 3];
			return (uint)(r | (g << 8) | (b << 16) | (a << 24));
		}

		
		public void SetPixel(int x, int y, uint rgba)
		{
			if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
			BlendPixel(x, y, rgba);
		}

		private unsafe void SetPixelUnchecked(int x, int y, uint rgba)
		{
			int i = (y * Width + x) * 4;
			fixed (byte* p = &Pixels[i])
			{
				*(uint*)p = rgba;
			}
		}

		private unsafe void BlendPixel(int x, int y, uint srcRgba)
		{
			int i = (y * Width + x) * 4;
			byte sr = (byte)(srcRgba & 0xFF);
			byte sg = (byte)((srcRgba >> 8) & 0xFF);
			byte sb = (byte)((srcRgba >> 16) & 0xFF);
			byte sa = (byte)((srcRgba >> 24) & 0xFF);

			fixed (byte* p = &Pixels[i])
			{
				if (sa == 255)
				{
					p[0] = sr;
					p[1] = sg;
					p[2] = sb;
					p[3] = 255;
					return;
				}
				if (sa == 0) return;

				float a = sa / 255f;
				float na = p[3] / 255f;
				float outA = a + na * (1 - a);
				if (outA <= 0f) { p[3] = 0; return; }

				float r = (sr * a + p[0] * na * (1 - a)) / outA;
				float g = (sg * a + p[1] * na * (1 - a)) / outA;
				float b = (sb * a + p[2] * na * (1 - a)) / outA;
				p[0] = ClampToByte(r);
				p[1] = ClampToByte(g);
				p[2] = ClampToByte(b);
				p[3] = ClampToByte(outA * 255f);
			}
		}

		private static byte ClampToByte(float v)
		{
			if (v <= 0f) return 0;
			if (v >= 255f) return 255;
			return (byte)(v + 0.5f);
		}

		public void DrawLine(int x0, int y0, int x1, int y1, uint color)
		{
			int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
			int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
			int err = dx + dy;
			while (true)
			{
				SetPixel(x0, y0, color);
				if (x0 == x1 && y0 == y1) break;
				int e2 = 2 * err;
				if (e2 >= dy) { err += dy; x0 += sx; }
				if (e2 <= dx) { err += dx; y0 += sy; }
			}
		}

		public void DrawRect(int x, int y, int w, int h, uint color, bool filled = false)
		{
			if (filled)
			{
				for (int yy = 0; yy < h; yy++)
				{
					for (int xx = 0; xx < w; xx++) SetPixel(x + xx, y + yy, color);
				}
			}
			else
			{
				DrawLine(x, y, x + w - 1, y, color);
				DrawLine(x, y, x, y + h - 1, color);
				DrawLine(x + w - 1, y, x + w - 1, y + h - 1, color);
				DrawLine(x, y + h - 1, x + w - 1, y + h - 1, color);
			}
		}

		public void DrawCircle(int cx, int cy, int r, uint color, bool filled = false)
		{
			int x = -r, y = 0, err = 2 - 2 * r;
			while (x <= 0)
			{
				if (filled)
				{
					for (int i = cx + x; i <= cx - x; i++)
					{
						SetPixel(i, cy + y, color);
						SetPixel(i, cy - y, color);
					}
				}
				else
				{
					SetPixel(cx - x, cy + y, color);
					SetPixel(cx + x, cy + y, color);
					SetPixel(cx + x, cy - y, color);
					SetPixel(cx - x, cy - y, color);
				}
				int e2 = err;
				if (e2 <= y) { y++; err += y * 2 + 1; }
				if (e2 > x || err > y) { x++; err += x * 2 + 1; }
			}
		}

		public void DrawPolygon(IReadOnlyList<(int x, int y)> points, uint color, bool filled = false)
		{
			if (points == null || points.Count < 2) return;
			if (!filled)
			{
				for (int i = 0; i < points.Count; i++)
				{
					var a = points[i];
					var b = points[(i + 1) % points.Count];
					DrawLine(a.x, a.y, b.x, b.y, color);
				}
				return;
			}

			int minY = int.MaxValue, maxY = int.MinValue;
			for (int i = 0; i < points.Count; i++)
			{
				int y = points[i].y;
				if (y < minY) minY = y;
				if (y > maxY) maxY = y;
			}
			for (int y = minY; y <= maxY; y++)
			{
				var nodes = _scanlineX; nodes.Clear();
				for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
				{
					int yi = points[i].y, yj = points[j].y;
					int xi = points[i].x, xj = points[j].x;
					bool cond = (yi < y && yj >= y) || (yj < y && yi >= y);
					if (cond)
					{
						int x = xi + (int)((y - yi) * (xj - (long)xi) / (double)(yj - yi));
						nodes.Add(x);
					}
				}
				nodes.Sort();
				for (int i = 0; i + 1 < nodes.Count; i += 2)
				{
					int x0 = nodes[i];
					int x1 = nodes[i + 1];
					for (int x = x0; x <= x1; x++) SetPixel(x, y, color);
				}
			}
		}

		static readonly List<int> _scanlineX = new List<int>(256);

		public void DrawText(int x, int y, string text, uint color)
		{
			if (string.IsNullOrEmpty(text)) return;
			int penX = x;
			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				if (ch == '\n') { y += Font.LineHeight; penX = x; continue; }
				DrawGlyph(penX, y, ch, color);
				penX += Font.Advance;
			}
		}

		private void DrawGlyph(int x, int y, char ch, uint color)
		{
			if (ch < 32 || ch > 126) ch = '?';
			int idx = ch - 32;
			int baseIndex = idx * Font.LineHeight;
			for (int row = 0; row < Font.LineHeight; row++)
			{
				byte bits = Font.ProggyClean5x7[baseIndex + row];
				for (int col = 0; col < Font.GlyphWidth; col++)
				{
					if (((bits >> (Font.GlyphWidth - 1 - col)) & 1) != 0)
					{
						SetPixel(x + col, y + row, color);
					}
				}
			}
		}

		public void Save(string path)
		{
			string ext = Path.GetExtension(path).ToLowerInvariant();
			switch (ext)
			{
				case ".bmp": SaveBmp(path); break;
				case ".jpg":
				case ".jpeg": ImageCodec.SaveJpeg(path, this); break;
				default: throw new NotSupportedException("Only .bmp, .jpg are supported");
			}
		}

		public static SoftwareBitmap Load(string path)
		{
			string ext = Path.GetExtension(path).ToLowerInvariant();
			switch (ext)
			{
				case ".bmp": return LoadBmp(path);
				case ".jpg":
				case ".jpeg": return ImageCodec.LoadJpeg(path);
				default: throw new NotSupportedException("Only .bmp, .jpg are supported");
			}
		}

		public void SaveBmp(string path)
		{
			int rowStride = Width * 3;
			int pad = (4 - (rowStride % 4)) & 3;
			int imgSize = (rowStride + pad) * Height;
			int fileSize = 14 + 40 + imgSize;
			using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
			using (var bw = new BinaryWriter(fs))
			{
				// BITMAPFILEHEADER
				bw.Write((byte)'B');
				bw.Write((byte)'M');
				bw.Write(fileSize);
				bw.Write(0);
				bw.Write(14 + 40);
				// BITMAPINFOHEADER (V3)
				bw.Write(40);
				bw.Write(Width);
				bw.Write(Height);
				bw.Write((short)1);
				bw.Write((short)24);
				bw.Write(0);
				bw.Write(imgSize);
				bw.Write(2835); // 72 DPI
				bw.Write(2835);
				bw.Write(0);
				bw.Write(0);

				byte[] padBytes = pad == 0 ? Array.Empty<byte>() : new byte[pad];
				byte[] row = new byte[rowStride];
				for (int y = Height - 1; y >= 0; y--)
				{
					int rowStart = y * Width * 4;
					for (int x = 0; x < Width; x++)
					{
						int i = rowStart + x * 4;
						row[x * 3 + 0] = Pixels[i + 2]; // B
						row[x * 3 + 1] = Pixels[i + 1]; // G
						row[x * 3 + 2] = Pixels[i + 0]; // R
					}
					bw.Write(row);
					if (pad != 0) bw.Write(padBytes);
				}
			}
		}

		public static SoftwareBitmap LoadBmp(string path)
		{
			using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
			using (var br = new BinaryReader(fs))
			{
				if (br.ReadByte() != 'B' || br.ReadByte() != 'M') throw new InvalidDataException("Not a BMP file");
				int fileSize = br.ReadInt32();
				br.ReadInt32(); // reserved
				int dataOffset = br.ReadInt32();
				int dibSize = br.ReadInt32();
				if (dibSize != 40) throw new NotSupportedException("Only BITMAPINFOHEADER supported");
				int width = br.ReadInt32();
				int height = br.ReadInt32();
				short planes = br.ReadInt16();
				short bpp = br.ReadInt16();
				int comp = br.ReadInt32();
				int imgSize = br.ReadInt32();
				br.ReadInt32(); // xppm
				br.ReadInt32(); // yppm
				br.ReadInt32(); // clrUsed
				br.ReadInt32(); // clrImportant
				if (planes != 1 || (bpp != 24 && bpp != 32) || comp != 0) throw new NotSupportedException("Only uncompressed 24/32-bit BMP supported");

				var bmp = new SoftwareBitmap(width, Math.Abs(height));
				bool bottomUp = height > 0;
				fs.Position = dataOffset;
				if (bpp == 24)
				{
					int rowStride = width * 3;
					int pad = (4 - (rowStride % 4)) & 3;
					for (int row = 0; row < bmp.Height; row++)
					{
						int y = bottomUp ? bmp.Height - 1 - row : row;
						for (int x = 0; x < width; x++)
						{
							byte b = br.ReadByte();
							byte g = br.ReadByte();
							byte r = br.ReadByte();
							bmp.SetPixel(x, y, (uint)(r | (g << 8) | (b << 16) | (255u << 24)));
						}
						if (pad != 0) br.ReadBytes(pad);
					}
				}
				else // 32
				{
					for (int row = 0; row < bmp.Height; row++)
					{
						int y = bottomUp ? bmp.Height - 1 - row : row;
						for (int x = 0; x < width; x++)
						{
							byte b = br.ReadByte();
							byte g = br.ReadByte();
							byte r = br.ReadByte();
							byte a = br.ReadByte();
							bmp.SetPixel(x, y, (uint)(r | (g << 8) | (b << 16) | ((uint)a << 24)));
						}
					}
				}
				return bmp;
			}
		}

		// New: draw an ellipse inside a rectangle.
		public void DrawEllipse(int x, int y, int w, int h, uint color, bool filled = false)
		{
			if (w <= 0 || h <= 0) return;
			// Midpoint ellipse algorithm
			int a2 = w * w;
			int b2 = h * h;
			int fx = 0;
			int fy = 2 * a2 * h;
			int px = 0;
			int py = (int)(a2 * h);
			int cx = x + w / 2;
			int cy = y + h / 2;
			int rx = w / 2;
			int ry = h / 2;

			int twoA2 = 2 * rx * rx;
			int twoB2 = 2 * ry * ry;
			int dx = 0;
			int dy = twoA2 * ry;
			int err = (int)(ry * ry - rx * rx * ry + 0.25 * rx * rx);
			int stopX = 0;
			int stopY = twoA2 * ry;

			int drawX(int xx) => cx + xx;
			int drawY(int yy) => cy + yy;

			int ix, iy;
			int x0 = 0;
			int y0 = ry;

			// Region 1
			while (stopX <= stopY)
			{
				if (filled)
				{
					for (ix = drawX(-x0); ix <= drawX(x0); ix++)
					{
						SetPixel(ix, drawY(y0), color);
						SetPixel(ix, drawY(-y0), color);
					}
				}
				else
				{
					SetPixel(drawX(x0), drawY(y0), color);
					SetPixel(drawX(-x0), drawY(y0), color);
					SetPixel(drawX(x0), drawY(-y0), color);
					SetPixel(drawX(-x0), drawY(-y0), color);
				}
				x0++;
				stopX += twoB2;
				err += stopX + b2;
				if (2 * err + ry * ry > 0)
				{
					y0--;
					stopY -= twoA2;
					err += a2 - stopY;
				}
			}

			// Region 2
			x0 = rx;
			y0 = 0;
			stopX = twoB2 * rx;
			stopY = 0;
			err = (int)(rx * rx - ry * ry * rx + 0.25 * ry * ry);
			while (stopX >= stopY)
			{
				if (filled)
				{
					for (iy = drawY(-y0); iy <= drawY(y0); iy++)
					{
						SetPixel(drawX(x0), iy, color);
						SetPixel(drawX(-x0), iy, color);
					}
				}
				else
				{
					SetPixel(drawX(x0), drawY(y0), color);
					SetPixel(drawX(-x0), drawY(y0), color);
					SetPixel(drawX(x0), drawY(-y0), color);
					SetPixel(drawX(-x0), drawY(-y0), color);
				}
				y0++;
				stopY += twoA2;
				err += stopY + a2;
				if (2 * err + rx * rx > 0)
				{
					x0--;
					stopX -= twoB2;
					err += b2 - stopX;
				}
			}
		}

		// New: convert to System.Drawing.Bitmap (32bpp ARGB). Copies RGBA -> BGRA.
		public unsafe Bitmap ToGdiBitmap()
		{
			var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
			var rect = new Rectangle(0, 0, Width, Height);
			var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			try
			{
				byte* dst = (byte*)data.Scan0;
				int srcIndex = 0;
				for (int y = 0; y < Height; y++)
				{
					byte* row = dst + y * data.Stride;
					for (int x = 0; x < Width; x++)
					{
						byte r = Pixels[srcIndex++];
						byte g = Pixels[srcIndex++];
						byte b = Pixels[srcIndex++];
						byte a = Pixels[srcIndex++];
						row[x * 4 + 0] = b;
						row[x * 4 + 1] = g;
						row[x * 4 + 2] = r;
						row[x * 4 + 3] = a;
					}
				}
			}
			finally
			{
				bmp.UnlockBits(data);
			}
			return bmp;
		}

		// New: create from System.Drawing.Bitmap (expects 32bppArgb; other formats are converted via cloning)
		public static unsafe SoftwareBitmap FromGdiBitmap(Bitmap src)
        {
            var w = src.Width;
            var h = src.Height;
			if (src.PixelFormat != PixelFormat.Format32bppArgb)
            {
                using var clone = src.Clone(new Rectangle(0, 0, w,h), PixelFormat.Format32bppArgb);
                return FromGdiBitmap(clone);
            }
			var sb = new SoftwareBitmap(src.Width, src.Height);
			var rect = new Rectangle(0, 0, src.Width, src.Height);
			var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try
			{
				byte* p = (byte*)data.Scan0;
				int di = 0;
				for (int y = 0; y < h; y++)
				{
					byte* row = p + y * data.Stride;
					for (int x = 0; x < w; x++)
					{
						byte b = row[x * 4 + 0];
						byte g = row[x * 4 + 1];
						byte r = row[x * 4 + 2];
						byte a = row[x * 4 + 3];
						sb.Pixels[di++] = r;
						sb.Pixels[di++] = g;
						sb.Pixels[di++] = b;
						sb.Pixels[di++] = a;
					}
				}
			}
			finally
			{
				src.UnlockBits(data);
			}
			return sb;
		}

		// New: draw bitmap blit at integer position (uses per-pixel alpha with global opacity)
		public void DrawBitmap(SoftwareBitmap src, int dstX, int dstY, float opacity = 1f)
		{
			if (opacity <= 0f) return;
			byte opa = (byte)ClampToByte(opacity * 255f);
			for (int y = 0; y < src.Height; y++)
			{
				int dy = dstY + y;
				if ((uint)dy >= (uint)Height) continue;
				for (int x = 0; x < src.Width; x++)
				{
					int dx = dstX + x;
					if ((uint)dx >= (uint)Width) continue;
					int si = (y * src.Width + x) * 4;
					byte sr = src.Pixels[si + 0];
					byte sg = src.Pixels[si + 1];
					byte sb = src.Pixels[si + 2];
					byte sa = src.Pixels[si + 3];
					byte a = (byte)((sa * opa) / 255);
					uint rgba = (uint)(sr | (sg << 8) | (sb << 16) | (a << 24));
					SetPixel(dx, dy, rgba);
				}
			}
		}

		// New: draw bitmap scaled into destination rect using nearest neighbor
		public void DrawBitmap(SoftwareBitmap src, int dstX, int dstY, int dstW, int dstH, float opacity = 1f)
		{
			if (opacity <= 0f || dstW <= 0 || dstH <= 0) return;
			byte opa = (byte)ClampToByte(opacity * 255f);
			for (int y = 0; y < dstH; y++)
			{
				int dy = dstY + y;
				if ((uint)dy >= (uint)Height) continue;
				int sy = (int)((long)y * src.Height / dstH);
				for (int x = 0; x < dstW; x++)
				{
					int dx = dstX + x;
					if ((uint)dx >= (uint)Width) continue;
					int sx = (int)((long)x * src.Width / dstW);
					int si = (sy * src.Width + sx) * 4;
					byte sr = src.Pixels[si + 0];
					byte sg = src.Pixels[si + 1];
					byte sb = src.Pixels[si + 2];
					byte sa = src.Pixels[si + 3];
					byte a = (byte)((sa * opa) / 255);
					uint rgba = (uint)(sr | (sg << 8) | (sb << 16) | (a << 24));
					SetPixel(dx, dy, rgba);
				}
			}
		}

		// New: draw bitmap transformed (centered at dstCx,dstCy) with size (dstW,dstH) and rotation in degrees
		public void DrawBitmapTransformed(SoftwareBitmap src, float dstCx, float dstCy, float dstW, float dstH, float rotationDeg, float opacity = 1f)
		{
			if (opacity <= 0f || dstW <= 0f || dstH <= 0f) return;
			byte opa = (byte)ClampToByte(opacity * 255f);
			float halfW = dstW * 0.5f;
			float halfH = dstH * 0.5f;
			float rad = rotationDeg * (float)Math.PI / 180f;
			float c = (float)Math.Cos(rad);
			float s = (float)Math.Sin(rad);
			// Compute bounding box of transformed quad
			( float x, float y )[] corners = new[]
			{
				(-halfW, -halfH), (halfW, -halfH), (halfW, halfH), (-halfW, halfH)
			};
			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			for (int i = 0; i < 4; i++)
			{
				float rx = c * corners[i].x - s * corners[i].y + dstCx;
				float ry = s * corners[i].x + c * corners[i].y + dstCy;
				if (rx < minX) minX = rx; if (rx > maxX) maxX = rx;
				if (ry < minY) minY = ry; if (ry > maxY) maxY = ry;
			}
			int ix0 = Math.Max(0, (int)Math.Floor(minX));
			int iy0 = Math.Max(0, (int)Math.Floor(minY));
			int ix1 = Math.Min(Width - 1, (int)Math.Ceiling(maxX));
			int iy1 = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
			if (ix1 < ix0 || iy1 < iy0) return;
			// Inverse map
			for (int y = iy0; y <= iy1; y++)
			{
				for (int x = ix0; x <= ix1; x++)
				{
					float dx = x - dstCx;
					float dy = y - dstCy;
					float ux =  ( c * dx + s * dy) / halfW; // before rotation src space normalized [-1,1]
					float uy =  (-s * dx + c * dy) / halfH;
					if (ux < -1f || ux > 1f || uy < -1f || uy > 1f) continue;
					float fx = (ux + 1f) * 0.5f * (src.Width - 1);
					float fy = (uy + 1f) * 0.5f * (src.Height - 1);
					int sx = (int)(fx + 0.5f);
					int sy = (int)(fy + 0.5f);
					int si = (sy * src.Width + sx) * 4;
					byte sr = src.Pixels[si + 0];
					byte sg = src.Pixels[si + 1];
					byte sb = src.Pixels[si + 2];
					byte sa = src.Pixels[si + 3];
					if (sa == 0) continue;
					byte a = (byte)((sa * opa) / 255);
					uint rgba = (uint)(sr | (sg << 8) | (sb << 16) | (a << 24));
					SetPixel(x, y, rgba);
				}
			}
		}

		public void DrawImage(SoftwareBitmap src, int x, int y)
		{
			if (src == null) return;
			DrawBitmap(src, x, y, 1f);
		}

		public void DrawImage(SoftwareBitmap src, int x, int y, float scale)
		{
			if (src == null) return;
			if (scale <= 0f) return;
			int dstW = Math.Max(1, (int)Math.Round(src.Width * scale));
			int dstH = Math.Max(1, (int)Math.Round(src.Height * scale));
			DrawBitmap(src, x, y, dstW, dstH, 1f);
		}

		public void DrawImage(SoftwareBitmap src, float centerX, float centerY, float rotationDeg, float scale)
		{
			if (src == null) return;
			if (scale <= 0f) return;
			float dstW = src.Width * scale;
			float dstH = src.Height * scale;
			DrawBitmapTransformed(src, centerX, centerY, dstW, dstH, rotationDeg, 1f);
		}
	}

	public static class Colors
	{
		public static uint Rgba(byte r, byte g, byte b, byte a = 255) => (uint)(r | (g << 8) | (b << 16) | (a << 24));
		public const uint Transparent = 0x00000000;
		public const uint Black = 0xFF000000;
		public const uint White = 0xFFFFFFFF;
		public const uint Red = 0xFF0000FF;
		public const uint Green = 0xFF00FF00;
		public const uint Blue = 0xFFFF0000;
		public const uint Yellow = 0xFF00FFFF;
		public const uint Magenta = 0xFFFF00FF;
		public const uint Cyan = 0xFFFFFF00;
	}

	internal static class ImageCodec
	{
		static bool _startupTried;
		static uint _token;
        internal static bool IsWindowsOS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
 
		public unsafe static SoftwareBitmap LoadJpeg(string path)
		{
			if (IsWindowsOS)
			{
				EnsureStartup();
				IntPtr img;
				int status = Gdip.GdipLoadImageFromFile(path, out img);
				if (status != 0) throw new InvalidOperationException("GDI+ load failed: " + status);
				try
				{
					uint w, h;
					Gdip.GdipGetImageWidth(img, out w);
					Gdip.GdipGetImageHeight(img, out h);
					var bmp = new SoftwareBitmap((int)w, (int)h);
					var rect = new Gdip.Rect() { X = 0, Y = 0, Width = (int)w, Height = (int)h };
					var data = new Gdip.BitmapData();
					status = Gdip.GdipBitmapLockBits(img, ref rect, (uint)Gdip.ImageLockMode.ReadOnly, (int)Gdip.PixelFormat.Format32bppARGB, ref data);
					if (status != 0) throw new InvalidOperationException("GDI+ LockBits failed: " + status);
					try
					{
						int srcStride = data.Stride;
						IntPtr scan0 = data.Scan0;
						fixed (byte* dst = &bmp.Pixels[0])
						{
							for (int y = 0; y < (int)h; y++)
							{
								IntPtr srcRow = IntPtr.Add(scan0, y * srcStride);
								int* srcPtr = (int*)srcRow;
								int* dstPtr = (int*)(dst + y * (int)w * 4);
								for (int x = 0; x < (int)w; x++)
								{
									dstPtr[x] = srcPtr[x];
								}
							}
						}
					}
					finally
					{
						Gdip.GdipBitmapUnlockBits(img, ref data);
					}
					return bmp;
				}
				finally
				{
					Gdip.GdipDisposeImage(img);
				}
			}
			else
			{
				return TurboJpeg.DecodeFromJpeg(File.ReadAllBytes(path));
			}
		}

		public static void SaveJpeg(string path, SoftwareBitmap src, int quality=90)
		{
			if (IsWindowsOS)
			{
				EnsureStartup();
				IntPtr unmanaged = IntPtr.Zero;
				IntPtr image = IntPtr.Zero;
				try
				{
					int w = src.Width, h = src.Height;
					int stride = w * 4;
					unmanaged = Marshal.AllocHGlobal(h * stride);
					for (int y = 0; y < h; y++)
					{
						int rowStart = (y * w) * 4;
						IntPtr dstRow = IntPtr.Add(unmanaged, y * stride);
						for (int x = 0; x < w; x++)
						{
							int i = rowStart + x * 4;
							byte r = src.Pixels[i + 0];
							byte g = src.Pixels[i + 1];
							byte b = src.Pixels[i + 2];
							byte a = src.Pixels[i + 3];
							Marshal.WriteByte(dstRow, x * 4 + 0, b);
							Marshal.WriteByte(dstRow, x * 4 + 1, g);
							Marshal.WriteByte(dstRow, x * 4 + 2, r);
							Marshal.WriteByte(dstRow, x * 4 + 3, a);
						}
					}
					int status = Gdip.GdipCreateBitmapFromScan0(w, h, stride, (int)Gdip.PixelFormat.Format32bppARGB, unmanaged, out image);
					if (status != 0) throw new InvalidOperationException("GDI+ CreateBitmapFromScan0 failed: " + status);

					var clsid = JpegClsid;
					status = Gdip.GdipSaveImageToFile(image, path, ref clsid, IntPtr.Zero);
					if (status != 0) throw new InvalidOperationException("GDI+ SaveImageToFile failed: " + status);
				}
				finally
				{
					if (image != IntPtr.Zero) Gdip.GdipDisposeImage(image);
					if (unmanaged != IntPtr.Zero) Marshal.FreeHGlobal(unmanaged);
				}
			}
			else
			{
				var bytes = TurboJpeg.EncodeToJpeg(src, quality);
				File.WriteAllBytes(path, bytes);
			}
		}

        public static byte[] SaveJpegToBytes(SoftwareBitmap src, int quality)
        {
            return IsWindowsOS ? 
                Utilities.EncodeToJpegWindows(src.Pixels, src.Width, src.Height, quality) : 
                TurboJpeg.EncodeToJpeg(src, quality <= 0 ? 90 : quality);
        }

		static void EnsureStartup()
		{
			if (_startupTried) return;
			_startupTried = true;
			if (!IsWindowsOS) return;
			var input = new Gdip.GdiplusStartupInput();
			input.GdiplusVersion = 1;
			uint token;
			int status = Gdip.GdiplusStartup(out token, ref input, IntPtr.Zero);
			if (status != 0) throw new InvalidOperationException("GDI+ startup failed: " + status);
			_token = token;
		}

		static Guid JpegClsid = new Guid(0x557CF401, 0x1A04, 0x11D3, 0x9A, 0x73, 0x00, 0x00, 0xF8, 0x1E, 0xF3, 0x2E);
		static Guid PngClsid  = new Guid(0x557CF406, 0x1A04, 0x11D3, 0x9A, 0x73, 0x00, 0x00, 0xF8, 0x1E, 0xF3, 0x2E);

		static class Gdip
		{
			[StructLayout(LayoutKind.Sequential)]
			internal struct GdiplusStartupInput
			{
				public uint GdiplusVersion;
				public IntPtr DebugEventCallback;
				public bool SuppressBackgroundThread;
				public bool SuppressExternalCodecs;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct Rect
			{
				public int X;
				public int Y;
				public int Width;
				public int Height;
			}

			[StructLayout(LayoutKind.Sequential)]
			internal struct BitmapData
			{
				public uint Width;
				public uint Height;
				public int Stride;
				public int PixelFormat;
				public IntPtr Scan0;
				public IntPtr Reserved;
			}

			internal enum ImageLockMode : uint
			{
				ReadOnly = 0x0001,
				WriteOnly = 0x0002,
				ReadWrite = 0x0003,
				UserInputBuf = 0x0004
			}

			internal enum PixelFormat
			{
				Format32bppARGB = 2498570 // from GDI+ headers
			}

			[DllImport("gdiplus", ExactSpelling = true, EntryPoint = "GdiplusStartup")]
			internal static extern int GdiplusStartup(out uint token, ref GdiplusStartupInput input, IntPtr output);
			[DllImport("gdiplus", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GdipLoadImageFromFile")]
			internal static extern int GdipLoadImageFromFile(string filename, out IntPtr image);
			[DllImport("gdiplus", ExactSpelling = true, EntryPoint = "GdipGetImageWidth")]
			internal static extern int GdipGetImageWidth(IntPtr image, out uint width);
			[DllImport("gdiplus", ExactSpelling = true, EntryPoint = "GdipGetImageHeight")]
			internal static extern int GdipGetImageHeight(IntPtr image, out uint height);
			[DllImport("gdiplus", ExactSpelling = true, EntryPoint = "GdipDisposeImage")]
			internal static extern int GdipDisposeImage(IntPtr image);
			[DllImport("gdiplus", ExactSpelling = true, EntryPoint = "GdipBitmapLockBits")]
			internal static extern int GdipBitmapLockBits(IntPtr bitmap, ref Rect rect, uint flags, int pixelFormat, ref BitmapData lockedBitmapData);
			[DllImport("gdiplus", ExactSpelling = true, EntryPoint = "GdipBitmapUnlockBits")]
			internal static extern int GdipBitmapUnlockBits(IntPtr bitmap, ref BitmapData lockedBitmapData);
			[DllImport("gdiplus", ExactSpelling = true, EntryPoint = "GdipCreateBitmapFromScan0")]
			internal static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr scan0, out IntPtr bitmap);
			[DllImport("gdiplus", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GdipSaveImageToFile")]
			internal static extern int GdipSaveImageToFile(IntPtr image, string filename, ref Guid clsid, IntPtr encoderParams);
		}
	}

	public static class Font
	{
		// A compact 5x7 monospaced bitmap font inspired by ProggyClean.
		// ASCII 32..126, stored row-major; each byte holds 5 LSBs as pixels.
		public const int GlyphWidth = 5;
		public const int LineHeight = 7;
		public const int Advance = 6; // 1px spacing

		public static readonly byte[] ProggyClean5x7 = new byte[]
		{
			// 32 ' '
			0,0,0,0,0,0,0,
			// 33 '!'
			0x04,0x04,0x04,0x04,0x00,0x00,0x04,
			// 34 '"'
			0x0A,0x0A,0x00,0x00,0x00,0x00,0x00,
			// 35 '#'
			0x0A,0x1F,0x0A,0x0A,0x1F,0x0A,0x0A,
			// 36 '$'
			0x04,0x0F,0x14,0x0E,0x05,0x1E,0x04,
			// 37 '%'
			0x18,0x19,0x02,0x04,0x08,0x13,0x03,
			// 38 '&'
			0x0C,0x12,0x14,0x08,0x15,0x12,0x0D,
			// 39 '\''
			0x06,0x04,0x08,0x00,0x00,0x00,0x00,
			// 40 '('
			0x02,0x04,0x08,0x08,0x08,0x04,0x02,
			// 41 ')'
			0x08,0x04,0x02,0x02,0x02,0x04,0x08,
			// 42 '*'
			0x00,0x04,0x15,0x0E,0x15,0x04,0x00,
			// 43 '+'
			0x00,0x04,0x04,0x1F,0x04,0x04,0x00,
			// 44 ','
			0x00,0x00,0x00,0x00,0x06,0x04,0x08,
			// 45 '-'
			0x00,0x00,0x00,0x1F,0x00,0x00,0x00,
			// 46 '.'
			0x00,0x00,0x00,0x00,0x00,0x06,0x06,
			// 47 '/'
			0x01,0x02,0x04,0x08,0x10,0x00,0x00,
			// 48 '0'
			0x0E,0x11,0x13,0x15,0x19,0x11,0x0E,
			// 49 '1'
			0x04,0x0C,0x04,0x04,0x04,0x04,0x0E,
			// 50 '2'
			0x0E,0x11,0x01,0x06,0x08,0x10,0x1F,
			// 51 '3'
			0x1F,0x02,0x04,0x02,0x01,0x11,0x0E,
			// 52 '4'
			0x02,0x06,0x0A,0x12,0x1F,0x02,0x02,
			// 53 '5'
			0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E,
			// 54 '6'
			0x06,0x08,0x10,0x1E,0x11,0x11,0x0E,
			// 55 '7'
			0x1F,0x01,0x02,0x04,0x08,0x08,0x08,
			// 56 '8'
			0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E,
			// 57 '9'
			0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C,
			// 58 ':'
			0x00,0x06,0x06,0x00,0x06,0x06,0x00,
			// 59 ';'
			0x00,0x06,0x06,0x00,0x06,0x04,0x08,
			// 60 '<'
			0x02,0x04,0x08,0x10,0x08,0x04,0x02,
			// 61 '='
			0x00,0x00,0x1F,0x00,0x1F,0x00,0x00,
			// 62 '>'
			0x08,0x04,0x02,0x01,0x02,0x04,0x08,
			// 63 '?'
			0x0E,0x11,0x01,0x06,0x04,0x00,0x04,
			// 64 '@'
			0x0E,0x11,0x01,0x0D,0x15,0x15,0x0E,
			// 65 'A'
			0x0E,0x11,0x11,0x1F,0x11,0x11,0x11,
			// 66 'B'
			0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E,
			// 67 'C'
			0x0E,0x11,0x10,0x10,0x10,0x11,0x0E,
			// 68 'D'
			0x1C,0x12,0x11,0x11,0x11,0x12,0x1C,
			// 69 'E'
			0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F,
			// 70 'F'
			0x1F,0x10,0x10,0x1E,0x10,0x10,0x10,
			// 71 'G'
			0x0E,0x11,0x10,0x17,0x11,0x11,0x0E,
			// 72 'H'
			0x11,0x11,0x11,0x1F,0x11,0x11,0x11,
			// 73 'I'
			0x0E,0x04,0x04,0x04,0x04,0x04,0x0E,
			// 74 'J'
			0x01,0x01,0x01,0x01,0x11,0x11,0x0E,
			// 75 'K'
			0x11,0x12,0x14,0x18,0x14,0x12,0x11,
			// 76 'L'
			0x10,0x10,0x10,0x10,0x10,0x10,0x1F,
			// 77 'M'
			0x11,0x1B,0x15,0x15,0x11,0x11,0x11,
			// 78 'N'
			0x11,0x19,0x15,0x13,0x11,0x11,0x11,
			// 79 'O'
			0x0E,0x11,0x11,0x11,0x11,0x11,0x0E,
			// 80 'P'
			0x1E,0x11,0x11,0x1E,0x10,0x10,0x10,
			// 81 'Q'
			0x0E,0x11,0x11,0x11,0x15,0x12,0x0D,
			// 82 'R'
			0x1E,0x11,0x11,0x1E,0x14,0x12,0x11,
			// 83 'S'
			0x0F,0x10,0x10,0x0E,0x01,0x01,0x1E,
			// 84 'T'
			0x1F,0x04,0x04,0x04,0x04,0x04,0x04,
			// 85 'U'
			0x11,0x11,0x11,0x11,0x11,0x11,0x0E,
			// 86 'V'
			0x11,0x11,0x11,0x11,0x11,0x0A,0x04,
			// 87 'W'
			0x11,0x11,0x11,0x15,0x15,0x1B,0x11,
			// 88 'X'
			0x11,0x11,0x0A,0x04,0x0A,0x11,0x11,
			// 89 'Y'
			0x11,0x11,0x11,0x0A,0x04,0x04,0x04,
			// 90 'Z'
			0x1F,0x01,0x02,0x04,0x08,0x10,0x1F,
			// 91 '['
			0x0E,0x08,0x08,0x08,0x08,0x08,0x0E,
			// 92 '\\'
			0x10,0x08,0x04,0x02,0x01,0x00,0x00,
			// 93 ']'
			0x0E,0x02,0x02,0x02,0x02,0x02,0x0E,
			// 94 '^'
			0x04,0x0A,0x11,0x00,0x00,0x00,0x00,
			// 95 '_'
			0x00,0x00,0x00,0x00,0x00,0x00,0x1F,
			// 96 '`'
			0x08,0x04,0x00,0x00,0x00,0x00,0x00,
			// 97 'a'
			0x00,0x00,0x0E,0x01,0x0F,0x11,0x0F,
			// 98 'b'
			0x10,0x10,0x1E,0x11,0x11,0x11,0x1E,
			// 99 'c'
			0x00,0x00,0x0E,0x10,0x10,0x11,0x0E,
			// 100 'd'
			0x01,0x01,0x0F,0x11,0x11,0x11,0x0F,
			// 101 'e'
			0x00,0x00,0x0E,0x11,0x1F,0x10,0x0E,
			// 102 'f'
			0x06,0x08,0x08,0x1E,0x08,0x08,0x08,
			// 103 'g'
			0x00,0x0F,0x11,0x11,0x0F,0x01,0x0E,
			// 104 'h'
			0x10,0x10,0x1E,0x11,0x11,0x11,0x11,
			// 105 'i'
			0x04,0x00,0x0C,0x04,0x04,0x04,0x0E,
			// 106 'j'
			0x02,0x00,0x06,0x02,0x02,0x12,0x0C,
			// 107 'k'
			0x10,0x10,0x12,0x14,0x18,0x14,0x12,
			// 108 'l'
			0x0C,0x04,0x04,0x04,0x04,0x04,0x0E,
			// 109 'm'
			0x00,0x00,0x1A,0x15,0x15,0x11,0x11,
			// 110 'n'
			0x00,0x00,0x1E,0x11,0x11,0x11,0x11,
			// 111 'o'
			0x00,0x00,0x0E,0x11,0x11,0x11,0x0E,
			// 112 'p'
			0x00,0x00,0x1E,0x11,0x11,0x1E,0x10,
			// 113 'q'
			0x00,0x00,0x0F,0x11,0x11,0x0F,0x01,
			// 114 'r'
			0x00,0x00,0x16,0x19,0x10,0x10,0x10,
			// 115 's'
			0x00,0x00,0x0F,0x10,0x0E,0x01,0x1E,
			// 116 't'
			0x08,0x08,0x1E,0x08,0x08,0x08,0x06,
			// 117 'u'
			0x00,0x00,0x11,0x11,0x11,0x11,0x0F,
			// 118 'v'
			0x00,0x00,0x11,0x11,0x11,0x0A,0x04,
			// 119 'w'
			0x00,0x00,0x11,0x11,0x15,0x1B,0x11,
			// 120 'x'
			0x00,0x00,0x11,0x0A,0x04,0x0A,0x11,
			// 121 'y'
			0x00,0x00,0x11,0x11,0x0F,0x01,0x0E,
			// 122 'z'
			0x00,0x00,0x1F,0x02,0x04,0x08,0x1F,
			// 123 '{'
			0x02,0x04,0x04,0x08,0x04,0x04,0x02,
			// 124 '|'
			0x04,0x04,0x04,0x00,0x04,0x04,0x04,
			// 125 '}'
			0x08,0x04,0x04,0x02,0x04,0x04,0x08,
			// 126 '~'
			0x00,0x00,0x08,0x15,0x02,0x00,0x00,
		};
	}

	// TurboJPEG accelerator (no managed dependency; requires libturbojpeg at runtime)
	internal static class TurboJpeg
	{
		internal static readonly bool Available;

		static TurboJpeg()
		{
			try
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
                    Console.WriteLine("> Linux system use libturbojpeg.so ...");
					string soPath = Utilities.FindLibraryPathLinux("libturbojpeg.so");
					if (soPath != null)
                    {
                        Utilities.LoadLibraryViaReflection(soPath);
						Available = true;
					}
				}
			}
			catch { Available = false; }
		}

		public static unsafe byte[] EncodeToJpeg(SoftwareBitmap src, int quality)
		{
			if (!Available) throw new DllNotFoundException("libturbojpeg not available");
			IntPtr handle = tjInitCompress();
			if (handle == IntPtr.Zero) throw new InvalidOperationException("tjInitCompress failed");
			try
			{
				IntPtr jpegBuf = IntPtr.Zero;
				ulong jpegSize = 0;
				int width = src.Width;
				int height = src.Height;
				int pitch = width * 4; // RGBX stride
				fixed (byte* p = src.Pixels)
				{
					int rc = tjCompress2(handle, (IntPtr)p, width, pitch, height, (int)TJPixelFormat.TJPF_RGBX, out jpegBuf, ref jpegSize, (int)TJSubsampling.TJSAMP_444, quality <= 0 ? 90 : quality, (int)TJFlags.TJFLAG_FASTDCT);
					if (rc != 0) throw new InvalidOperationException("tjCompress2 failed");
				}
				try
				{
					byte[] managed = new byte[(int)jpegSize];
					Marshal.Copy(jpegBuf, managed, 0, (int)jpegSize);
					return managed;
				}
				finally
				{
					if (jpegBuf != IntPtr.Zero) tjFree(jpegBuf);
				}
			}
			finally
			{
				tjDestroy(handle);
			}
		}

		public static unsafe SoftwareBitmap DecodeFromJpeg(byte[] jpeg)
		{
			if (!Available) throw new DllNotFoundException("libturbojpeg not available");
			fixed (byte* pJ = jpeg)
			{
				IntPtr handle = tjInitDecompress();
				if (handle == IntPtr.Zero) throw new InvalidOperationException("tjInitDecompress failed");
				try
				{
					int width, height, jpegSubsamp, colorspace;
					int rc = tjDecompressHeader3(handle, (IntPtr)pJ, jpeg.Length, out width, out height, out jpegSubsamp, out colorspace);
					if (rc != 0) throw new InvalidOperationException("tjDecompressHeader3 failed");
					var bmp = new SoftwareBitmap(width, height);
					fixed (byte* pOut = bmp.Pixels)
					{
						rc = tjDecompress2(handle, (IntPtr)pJ, jpeg.Length, (IntPtr)pOut, width, width * 4, height, (int)TJPixelFormat.TJPF_RGBX, (int)TJFlags.TJFLAG_FASTDCT);
						if (rc != 0) throw new InvalidOperationException("tjDecompress2 failed");
					}
					return bmp;
				}
				finally
				{
					tjDestroy(handle);
				}
			}
		}

		[DllImport("libturbojpeg.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr tjInitCompress();

		[DllImport("libturbojpeg.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern int tjCompress2(IntPtr handle, IntPtr srcBuf, int width, int pitch, int height, int pixelFormat, out IntPtr jpegBuf, ref ulong jpegSize, int jpegSubsamp, int jpegQual, int flags);

		[DllImport("libturbojpeg.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern void tjFree(IntPtr buffer);

		[DllImport("libturbojpeg.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern int tjDestroy(IntPtr handle);

		[DllImport("libturbojpeg.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr tjInitDecompress();

		[DllImport("libturbojpeg.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern int tjDecompressHeader3(IntPtr handle, IntPtr jpegBuf, int jpegSize, out int width, out int height, out int jpegSubsamp, out int colorspace);

		[DllImport("libturbojpeg.so.0", CallingConvention = CallingConvention.Cdecl)]
		private static extern int tjDecompress2(IntPtr handle, IntPtr jpegBuf, int jpegSize, IntPtr dstBuf, int width, int pitch, int height, int pixelFormat, int flags);

		private enum TJPixelFormat
		{
			TJPF_RGB = 0,
			TJPF_BGR = 1,
			TJPF_RGBX = 2,
			TJPF_BGRX = 3,
			TJPF_XBGR = 4,
			TJPF_XRGB = 5,
			TJPF_GRAY = 6,
			TJPF_RGBA = 7,
			TJPF_BGRA = 8,
			TJPF_ABGR = 9,
			TJPF_ARGB = 10
		}

		private enum TJSubsampling
		{
			TJSAMP_444 = 0,
			TJSAMP_422 = 1,
			TJSAMP_420 = 2,
			TJSAMP_GRAY = 3,
			TJSAMP_440 = 4
		}

		[Flags]
		private enum TJFlags
		{
			TJFLAG_BOTTOMUP = 2,
			TJFLAG_FASTDCT = 2048
		}
	}
} 