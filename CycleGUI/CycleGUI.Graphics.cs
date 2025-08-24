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
				case ".png": ImageCodec.SavePng(path, this); break;
				default: throw new NotSupportedException("Only .bmp, .jpg and .png are supported");
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
				case ".png": return ImageCodec.LoadPng(path);
				default: throw new NotSupportedException("Only .bmp, .jpg and .png are supported");
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
	}

	internal static class ImageCodec
	{
		static bool _startupTried;
		static uint _token;
        internal static bool IsWindowsOS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
 
		public static SoftwareBitmap LoadJpeg(string path)
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
						for (int y = 0; y < (int)h; y++)
						{
							IntPtr srcRow = IntPtr.Add(scan0, y * srcStride);
							for (int x = 0; x < (int)w; x++)
							{
								byte b = Marshal.ReadByte(srcRow, x * 4 + 0);
								byte g = Marshal.ReadByte(srcRow, x * 4 + 1);
								byte r = Marshal.ReadByte(srcRow, x * 4 + 2);
								byte a = Marshal.ReadByte(srcRow, x * 4 + 3);
								bmp.SetPixel(x, y, (uint)(r | (g << 8) | (b << 16) | ((uint)a << 24)));
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
				return LibGD.LoadJpeg(path);
			}
		}

		public static SoftwareBitmap LoadPng(string path)
		{
			if (IsWindowsOS)
			{
				return LoadJpeg(path); // GDI+ handles PNG as well
			}
			else
			{
				return LibGD.LoadPng(path);
			}
		}

		public static void SaveJpeg(string path, SoftwareBitmap src)
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
				LibGD.SaveJpeg(path, src, 90);
			}
		}

		public static void SavePng(string path, SoftwareBitmap src)
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

					var clsid = PngClsid;
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
				LibGD.SavePng(path, src);
			}
		}

        public static byte[] SaveJpegToBytes(SoftwareBitmap src, int quality)
        {
            return IsWindowsOS ? 
                Utilities.EncodeToJpegWindows(src.Pixels, src.Width, src.Height, quality) : 
                LibGD.SaveJpegToBytes(src, quality <= 0 ? 90 : quality);
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

		internal static class LibGD
		{
			// libgd P/Invoke
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern IntPtr gdImageCreateTrueColor(int sx, int sy);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern void gdImageDestroy(IntPtr im);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern void gdImageSaveAlpha(IntPtr im, int saveFlag);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern void gdImageAlphaBlending(IntPtr im, int blending);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern void gdImageSetPixel(IntPtr im, int x, int y, int color);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern int gdImageGetTrueColorPixel(IntPtr im, int x, int y);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern int gdImageGetWidth(IntPtr im);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern int gdImageGetHeight(IntPtr im);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern IntPtr gdImagePngPtr(IntPtr im, out int size);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern IntPtr gdImageJpegPtr(IntPtr im, out int size, int quality);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern void gdFree(IntPtr m);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern IntPtr gdImageCreateFromPngPtr(int size, IntPtr data);
			[DllImport("libgd.so.3", CallingConvention = CallingConvention.Cdecl)]
			static extern IntPtr gdImageCreateFromJpegPtr(int size, IntPtr data);

			static int A8ToGdAlpha(byte a8)
			{
				// 0..255 (opaque..transparent) -> 0..127 (opaque..transparent)
				int inv = 255 - a8;
				return (inv * 127 + 127 / 2) / 255;
			}

			static byte GdToA8(int gdAlpha)
			{
				// 0..127 (opaque..transparent) -> 0..255 (opaque..transparent)
				int inv = (gdAlpha * 255 + 63) / 127;
				int a = 255 - inv;
				if (a < 0) a = 0; if (a > 255) a = 255;
				return (byte)a;
			}

            static LibGD()
            {

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    return;

                Console.WriteLine("> Running on Linux, locating libgd via ldconfig...");

                string soPath = Utilities.FindLibraryPathLinux("libgd.so");
                if (soPath == null)
                    throw new DllNotFoundException("libgd not found via ldconfig.");

                Utilities.LoadLibraryViaReflection(soPath);

                Console.WriteLine($"> Loaded {soPath}");
            }

			public static SoftwareBitmap LoadPng(string path)
			{
				byte[] bytes = File.ReadAllBytes(path);
				GCHandle h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
				try
				{
					IntPtr im = gdImageCreateFromPngPtr(bytes.Length, h.AddrOfPinnedObject());
					if (im == IntPtr.Zero) throw new InvalidOperationException("gdImageCreateFromPngPtr failed");
					try
					{
						int w = gdImageGetWidth(im);
						int hgt = gdImageGetHeight(im);
						var bmp = new SoftwareBitmap(w, hgt);
						unsafe
						{
							fixed (byte* p = bmp.Pixels)
							{
								for (int y = 0; y < hgt; y++)
								{
									int row = y * w * 4;
									for (int x = 0; x < w; x++)
									{
										int c = gdImageGetTrueColorPixel(im, x, y);
										int ga = (c >> 24) & 0x7F;
										byte a8 = GdToA8(ga);
										p[row + x * 4 + 0] = (byte)((c >> 16) & 0xFF);
										p[row + x * 4 + 1] = (byte)((c >> 8) & 0xFF);
										p[row + x * 4 + 2] = (byte)(c & 0xFF);
										p[row + x * 4 + 3] = a8;
									}
								}
							}
						}
						return bmp;
					}
					finally
					{
						gdImageDestroy(im);
					}
				}
				finally { h.Free(); }
			}

			public static SoftwareBitmap LoadJpeg(string path)
			{
				byte[] bytes = File.ReadAllBytes(path);
				GCHandle h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
				try
				{
					IntPtr im = gdImageCreateFromJpegPtr(bytes.Length, h.AddrOfPinnedObject());
					if (im == IntPtr.Zero) throw new InvalidOperationException("gdImageCreateFromJpegPtr failed");
					try
					{
						int w = gdImageGetWidth(im);
						int hgt = gdImageGetHeight(im);
						var bmp = new SoftwareBitmap(w, hgt);
						unsafe
						{
							fixed (byte* p = bmp.Pixels)
							{
								for (int y = 0; y < hgt; y++)
								{
									int row = y * w * 4;
									for (int x = 0; x < w; x++)
									{
										int c = gdImageGetTrueColorPixel(im, x, y);
										int ga = (c >> 24) & 0x7F;
										byte a8 = GdToA8(ga);
										p[row + x * 4 + 0] = (byte)((c >> 16) & 0xFF);
										p[row + x * 4 + 1] = (byte)((c >> 8) & 0xFF);
										p[row + x * 4 + 2] = (byte)(c & 0xFF);
										p[row + x * 4 + 3] = a8;
									}
								}
							}
						}
						return bmp;
					}
					finally
					{
						gdImageDestroy(im);
					}
				}
				finally { h.Free(); }
			}

			public static void SavePng(string path, SoftwareBitmap src)
			{
				IntPtr im = gdImageCreateTrueColor(src.Width, src.Height);
				if (im == IntPtr.Zero) throw new InvalidOperationException("gdImageCreateTrueColor failed");
				try
				{
					gdImageAlphaBlending(im, 0);
					gdImageSaveAlpha(im, 1);
					for (int y = 0; y < src.Height; y++)
					{
						int row = y * src.Width * 4;
						for (int x = 0; x < src.Width; x++)
						{
							int i = row + x * 4;
							byte r = src.Pixels[i + 0];
							byte g = src.Pixels[i + 1];
							byte b = src.Pixels[i + 2];
							byte a = src.Pixels[i + 3];
							int ga = A8ToGdAlpha(a);
							int color = (ga << 24) | (r << 16) | (g << 8) | b;
							gdImageSetPixel(im, x, y, color);
						}
					}
					int size;
					IntPtr data = gdImagePngPtr(im, out size);
					if (data == IntPtr.Zero) throw new InvalidOperationException("gdImagePngPtr failed");
					try
					{
						byte[] managed = new byte[size];
						Marshal.Copy(data, managed, 0, size);
						File.WriteAllBytes(path, managed);
					}
					finally { gdFree(data); }
				}
				finally { gdImageDestroy(im); }
			}

			public static void SaveJpeg(string path, SoftwareBitmap src, int quality)
			{
				IntPtr im = gdImageCreateTrueColor(src.Width, src.Height);
				if (im == IntPtr.Zero) throw new InvalidOperationException("gdImageCreateTrueColor failed");
				try
				{
					gdImageAlphaBlending(im, 0);
					for (int y = 0; y < src.Height; y++)
					{
						int row = y * src.Width * 4;
						for (int x = 0; x < src.Width; x++)
						{
							int i = row + x * 4;
							byte r = src.Pixels[i + 0];
							byte g = src.Pixels[i + 1];
							byte b = src.Pixels[i + 2];
							byte a = src.Pixels[i + 3];
							int ga = A8ToGdAlpha(a);
							int color = (ga << 24) | (r << 16) | (g << 8) | b;
							gdImageSetPixel(im, x, y, color);
						}
					}
					int size;
					IntPtr data = gdImageJpegPtr(im, out size, quality);
					if (data == IntPtr.Zero) throw new InvalidOperationException("gdImageJpegPtr failed");
					try
					{
						byte[] managed = new byte[size];
						Marshal.Copy(data, managed, 0, size);
						File.WriteAllBytes(path, managed);
					}
					finally { gdFree(data); }
				}
				finally { gdImageDestroy(im); }
			}

			public static byte[] SaveJpegToBytes(SoftwareBitmap src, int quality)
			{
				IntPtr im = gdImageCreateTrueColor(src.Width, src.Height);
				if (im == IntPtr.Zero) throw new InvalidOperationException("gdImageCreateTrueColor failed");
				try
				{
					gdImageAlphaBlending(im, 0);
					for (int y = 0; y < src.Height; y++)
					{
						int row = y * src.Width * 4;
						for (int x = 0; x < src.Width; x++)
						{
							int i = row + x * 4;
							byte r = src.Pixels[i + 0];
							byte g = src.Pixels[i + 1];
							byte b = src.Pixels[i + 2];
							byte a = src.Pixels[i + 3];
							int ga = A8ToGdAlpha(a);
							int color = (ga << 24) | (r << 16) | (g << 8) | b;
							gdImageSetPixel(im, x, y, color);
						}
					}
					int size;
					IntPtr data = gdImageJpegPtr(im, out size, quality);
					if (data == IntPtr.Zero) throw new InvalidOperationException("gdImageJpegPtr failed");
					try
					{
						byte[] managed = new byte[size];
						Marshal.Copy(data, managed, 0, size);
						return managed;
					}
					finally { gdFree(data); }
				}
				finally { gdImageDestroy(im); }
			}
		}

		// Back-compat wrapper to keep existing references working
		internal static class JpegCodec
		{
			public static SoftwareBitmap LoadJpeg(string path) => ImageCodec.LoadJpeg(path);
			public static SoftwareBitmap LoadPng(string path) => ImageCodec.LoadPng(path);
			public static void SaveJpeg(string path, SoftwareBitmap src) => ImageCodec.SaveJpeg(path, src);
			public static void SavePng(string path, SoftwareBitmap src) => ImageCodec.SavePng(path, src);
			public static byte[] SaveJpegToBytes(SoftwareBitmap src, int quality) => ImageCodec.SaveJpegToBytes(src, quality);
			internal static bool IsWindowsOS => ImageCodec.IsWindowsOS;
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
} 