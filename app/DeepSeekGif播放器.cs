using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace DeepSeekWhaleWpf
{
    // GIF is a portable asset format; the WPF window presents its decoded frames
    // at up to 120 fps. Actual presentation is limited by monitor refresh rate.
    internal sealed class GifClip
    {
        public readonly string FileName;
        public readonly IList<BitmapFrame> Frames;

        private GifClip(string fileName, IList<BitmapFrame> frames)
        {
            FileName = fileName;
            Frames = frames;
        }

        public static GifClip Load(string fileName)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "animations", fileName);
                if (!File.Exists(path)) return null;
                using (FileStream stream = File.OpenRead(path))
                {
                    GifBitmapDecoder decoder = new GifBitmapDecoder(stream,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    List<BitmapFrame> frames = new List<BitmapFrame>();
                    foreach (BitmapFrame frame in decoder.Frames)
                    {
                        frame.Freeze();
                        frames.Add(frame);
                    }
                    return frames.Count == 0 ? null : new GifClip(fileName, frames);
                }
            }
            catch (Exception) { return null; }
        }
    }
}
