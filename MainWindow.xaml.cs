using ImageMagick;
using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Tesseract;

namespace ArknightsTagMarker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref Rect rectangle);

        public struct Rect
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Right { get; set; }
            public int Bottom { get; set; }
        }

        Rect CapturedWindowRect = new Rect();
        IntPtr Ptr = new IntPtr();

        /// <summary>
        /// Number of boxes containing tags in game
        /// </summary>
        const int BoxCount = 5;
        Vector2[] TagBoxes = new Vector2[BoxCount];

        // made based on this https://www.reddit.com/r/arknights/comments/1m1xsrj/recruitment_tag_quick_reference_guide/
        HashSet<string> Solo4StarTags = ["CrowdControl", "Debuff", "Nuker", "Shift", "Support", "Specialist", "Summon", "FastRedeploy"];

        Dictionary<string, (string, Rarity)[]> Tag2Combos = new Dictionary<string, (string, Rarity)[]>
        {
            { "Slow", [("AOE", Rarity.Star4), ("Caster", Rarity.Star4), ("DPS", Rarity.Star4), ("Guard", Rarity.Star4), ("Healing", Rarity.Star4), ("Melee", Rarity.Star4), ("Sniper", Rarity.Star4)] },
            { "DPS", [("Defender", Rarity.Star5), ("Defense", Rarity.Star5), ("Healing", Rarity.Star5), ("Supporter", Rarity.Star5), ("AOE", Rarity.Star4)] },
            { "Defense", [("AOE", Rarity.Star5), ("Caster", Rarity.Star5), ("Guard", Rarity.Star5), ("Ranged", Rarity.Star5), ("Survival", Rarity.Star5)] },
            { "Survival", [("Defender", Rarity.Star5), ("Supporter", Rarity.Star5), ("Ranged", Rarity.Star4), ("Sniper", Rarity.Star4)] },
            { "Healing", [("Caster", Rarity.Star5), ("DPRecovery", Rarity.Star4), ("Supporter", Rarity.Star4), ("Vanguard", Rarity.Star4)] },
            { "Ranged", [("DPRecovery", Rarity.Star4), ("Vanguard", Rarity.Star4)] },
            { "Crowd Control", [("DPRecovery", Rarity.Star5), ("FastRedeploy", Rarity.Star5), ("Melee", Rarity.Star5), ("Slow", Rarity.Star5), ("Specialist", Rarity.Star5), ("Summon", Rarity.Star5), ("Supporter", Rarity.Star5), ("Vanguard", Rarity.Star5)] },
            { "Debuff", [("AOE", Rarity.Star5), ("FastRedeploy", Rarity.Star5), ("Melee", Rarity.Star5), ("Specialist", Rarity.Star5), ("Supporter", Rarity.Star5)] },
            { "Nuker", [("AOE", Rarity.Star5), ("Caster", Rarity.Star5), ("Ranged", Rarity.Star5), ("Sniper", Rarity.Star5)] },
            { "Shift", [("DPS", Rarity.Star5), ("Defender", Rarity.Star5), ("Defense", Rarity.Star5), ("Slow", Rarity.Star5)] },
            { "Support", [("DPRecovery", Rarity.Star5), ("Supporter", Rarity.Star5), ("Survival", Rarity.Star5), ("Vanguard", Rarity.Star5)] },
            { "Specialist", [("Slow", Rarity.Star5), ("Survival", Rarity.Star5)] },
            { "Summon", [("Supporter", Rarity.Star5)] },
        };

        List<((string, string, string), Rarity)> Tag3Combos = new List<((string, string, string), Rarity)>
        {
           { (( "Caster","Slow", "DPS"), Rarity.Star5) },
           { (( "AOE", "DPS", "Guard"), Rarity.Star5) },
           { (( "AOE", "DPS", "Melee"), Rarity.Star5) },
        };

        TesseractEngine Engine;
        MagickReadSettings MagickReadSettings = new MagickReadSettings();
        MorphologySettings MorphologySettings;

        // yes i know code can be better, yes i know its not perfectly optimal, yes i can probably have update loop take <100ms instad of <200ms
        // and yes I DONT CARE about any of that in this small app that is supposed to be launched for 15s
        public MainWindow()
        {
            InitializeComponent();

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(200);
            timer.Tick += Update;

            // what program window should follow here, if program to follow doesnt exist app WILL (intended) crash into oblivion
            Process[] processes = Process.GetProcessesByName("dnplayer");
            Ptr = processes[0].MainWindowHandle;

            // sigh https://github.com/charlesw/tesseract/issues/636#event-1299319774
            TesseractEnviornment.CustomSearchPath = Environment.CurrentDirectory;

            Engine = new TesseractEngine($"{TesseractEnviornment.CustomSearchPath}\\tessdata", "eng", EngineMode.Default);

            // accuracy options test
            // single column > single line > raw line
            //  ^ seems best from all the options other ones either dont work or are just worse than best option
            

            MagickReadSettings.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
            MorphologySettings = new MorphologySettings()
            {
                Iterations = 2, // 1 was too little and 2 seems perfect
                Kernel = Kernel.Diamond,
                Method = MorphologyMethod.Erode,
            };

            timer.Start();
        }

        Stopwatch w = new Stopwatch();
        private void Update(object? sender, EventArgs e)
        {
            #if DEBUG   
            w.Start();
            #endif

            GetWindowRect(Ptr, ref CapturedWindowRect);
            MoveWindow();
            ResizeTagBoxes();
            UpdateTagBoxesPositionData();
            ResizeResultBoxFontSize();

            try
            {
                for (int i = 0; i < BoxCount; i++)
                {
                    // after tweeking a lot of settings for hours this seems very good... slow but it works very well
                    MagickReadSettings.ExtractArea = new MagickGeometry((int)TagBoxes[i].X, (int)TagBoxes[i].Y, (uint)(Width * 0.17), (uint)(Height * 0.13));
                    //MagickReadSettings.ExtractArea = new MagickGeometry((int)TagBoxes[i].X, (int)TagBoxes[i].Y, (uint)(Width * 0.19), (uint)(Height * 0.13));
                    using (MagickImage image = new MagickImage("SCREENSHOT:", MagickReadSettings))
                    {
                        image.Grayscale(); // when box becomes blue (selected) this improves OCR accuracy

                        // maybe dynamically adjust this so image size will always be the same... too big = bad and too small = also bad
                        // 400 seems pretty good there with all other configurations i have set up
                        image.Scale(new Percentage(100 / (image.Width / 400.0)));
                        //image.Morphology(MorphologySettings);
                        //image.AdaptiveSharpen(0, 30.0);
                        image.UnsharpMask(100, 10);
                        Engine.DefaultPageSegMode = PageSegMode.SingleWord;
                        // this is slow(? 50ms for all images) but idk how else i can do it
                        // is this love in the air? no its RAM leak *PC explodes*... would be nice if there was info this is disposable
                        using (IPixelCollection<byte> pixels = image.GetPixels())
                        {
                            foreach (IPixel<byte> pixel in pixels)
                            {
                                IMagickColor<byte>? currentPixelColour = pixel.ToColor();
                                if (currentPixelColour.R < 50 && currentPixelColour.G < 50 && currentPixelColour.B < 50)
                                {
                                    // too dark(low) = bad, too bright(high) = bad, 40 seems perfect
                                    pixel.SetChannel(0, 40);
                                }
                            }
                        }

                        // idk if format matters coz from what i tested everything seems the same... saw that someone wrote
                        // that .tiff has best accuracy but i didnt see difference between tiff, png and jpeg... but will trust this
                        // random internet person anyway since i know nothing about that stuff and it was in topic of Tesseract OCR
                        image.Write($"banana{i}.tiff", MagickFormat.Tiff);
                    }
                }

                MarkTag();
            } catch { } // there might be some exceptions and crashes but i cant care enough to looks for them since they dont break the app

            #if DEBUG
            w.Stop();
            Console.WriteLine(w.ElapsedMilliseconds);
            w.Reset();
            #endif
        }

        public string ExtractedText()
        {
            string text = "";
            for (int i = 0; i < BoxCount; i++)
            {
                Pix img = Pix.LoadFromFile($"banana{i}.tiff");
                
                // doesnt really help but leaving it here to know this even exists
                //Pix grayImage = scaledImage.ConvertRGBToGray();
                //Pix thresholdedPix = grayImage.BinarizeOtsuAdaptiveThreshold(16, 16, 0, 0, 1.0f);

                Tesseract.Page page = Engine.Process(img);
                string pageText = page.GetText();
                if (pageText != "")
                {
                    string hold = text;
                    string temp = hold + pageText + (i < BoxCount - 1 ? "," : "");
                    text = temp;
                }
                
                page.Dispose();
                img.Dispose();
            }

            return text;
        }

        public void MarkTag()
        {
            // regex to reduce random noise characters that appear                               im sorry but WHY???        i dont care IT WORKS and .| doesnt
            string OCRTags = Regex.Replace(ExtractedText(), @$"\t|\n|\r|Q|\|;|-|{(char)45}|:|`|'|_|‘|{(char)8212}|I| |", "").Replace(".", "");

            // for testing
            //string OCRTags = "Shift,DPS,FastRedeploy,AOE,Slow";
            //string OCRTags = "Meleee,PS,FastERedeploy,ABE,Slaow";
            //string OCRTags = "Melee,DPS,FastRedeploy,AOE,Slow";
            //string OCRTags = "DPS,DPRecovery,FastRedeploy,AOE,Slow";
            //string OCRTags = "Support,Supporter,FastRedeploy,AOE,Slow";

            string[] tags = OCRTags.Split(",");

            TextBox4StarTags.Text = "4* Tags: ";
            TextBox5StarTags.Text = "5* Tags: ";

            // noise reduction part 2 woweee
            for (int i = 0; i < tags.Length; i++)
            {
                try
                {
                    // sometimes it still goes through when its length is 0... funny
                    if (tags[i].Length <= 0)
                    {
                        continue;
                    }

                    // noise character removal at first index
                    if (tags[i][0] == 'C' && tags[i][1] != 'r')
                    {
                        string newTagName = tags[i].Remove(0, 1);
                        tags[i] = newTagName;
                    }

                    // this is one character??? H O W ??? sure i guess you learn something new everyday
                    //if (tags[i][0] == 'ﬁ')
                    //{
                    //    string newTagName = tags[i].Remove(0, 1);
                    //    tags[i] = newTagName;
                    //}

                    // all tags start with higher case letters
                    if (char.IsLower(tags[i][0]) || tags[i][0] == '|')
                    {
                        string newTagName = tags[i].Remove(0, 1);
                        tags[i] = newTagName;
                    }
                }
                catch { } // just so app doesnt crash and continues working in rare situations
            }

            #if DEBUG
            for (int i = 0; i < tags.Length; i++)
            {
                Console.WriteLine(tags[i]);
            }
            #endif

            (string, Rarity)[] combos = null!;
            // so many loops!
            for (int i = 0; i < tags.Length; i++)
            {
                foreach (string tag in Solo4StarTags)
                {
                    if (IsSame(tag, tags[i]))
                    {
                        AddTagsToBox(Rarity.Star4, tag);
                    }
                }
            
                foreach (string tag in Tag2Combos.Keys)
                {
                    if (IsSame(tag, tags[i]))
                    {
                        tags[i] = tag; // sometimes this tag might have errors to this fixes these possible errors
                        combos = Tag2Combos[tags[i]];
                    }
                }
            
                for (int j = 0; j < tags.Length; j++)
                {
                    if (combos != null)
                    {
                        foreach ((string tag, Rarity r) s in combos)
                        {
                            if (i != j && IsSame(s.tag, tags[j]))
                            {
                                AddTagsToBox(s.r, tags[i], s.tag);
                            }
                        }
                    }
            
                    // for EXTREMELY rare 3 tag combo
                    for (int k = 0; k < tags.Length; k++)
                    {
                        foreach (((string tag1, string tag2, string tag3), Rarity r) s in Tag3Combos)
                        {
                            if (IsSame(s.Item1.tag1, tags[i]) && IsSame(s.Item1.tag2, tags[j]) && IsSame(s.Item1.tag3, tags[k]))
                            {
                                AddTagsToBox(s.r, s.Item1.tag1, s.Item1.tag2, s.Item1.tag3);
                            }
                        }
                    }
                }
            
                combos = null!;
            }
        }

        public bool IsSame(string tag1, string tag2)
        {
            // return early if character difference is higher than 1
            if (Math.Abs(tag1.Length - tag2.Length) > 1)
            {
                return false;
            }

            int matchedChars = 0;
            int wrongCharCount = 0;

            int appendIndex = 0;
            int j = 0;
            int i = 0;
            while (true)
            {
                j = i + appendIndex;
                if (i + 1 == tag1.Length && j + appendIndex + 1 < tag2.Length)
                {
                    appendIndex++;
                }

                if (tag1[i] == tag2[j])
                {
                    matchedChars++;
                }
                else
                {
                    wrongCharCount++;
                    if (wrongCharCount > 1) // more than one mistake so end loop early
                    {
                        break;
                    }

                    if (tag1.Length > tag2.Length)
                    {
                        appendIndex--;
                    }
                    else if (tag1.Length < tag2.Length)
                    {
                        appendIndex++;
                    }
                }

                if (i + 1 < tag1.Length)
                {
                    i++;
                }

                // i have no clue how to better quit this while loop but it works so
                if (i + 1 >= tag1.Length && j + appendIndex + 1 >= tag2.Length)
                {
                    break;
                }
            }

            if (wrongCharCount <= 1) // 1 letter can be wrong
            {
                return true;
            }

            return false;
        }

        public void AddTagsToBox(Rarity r, string tag1 = "", string tag2 = "", string tag3 = "")
        {
            if (tag3 != "")
            {
                if (r == Rarity.Star4)
                {
                    string hold = TextBox4StarTags.Text;
                    string temp = hold + $"({tag1}, {tag2}, {tag3})";
                    TextBox4StarTags.Text = temp;
                }
                else
                {
                    string hold = TextBox5StarTags.Text;
                    string temp = hold + $"({tag1}, {tag2}, {tag3})";
                    TextBox5StarTags.Text = temp;
                }
            }
            else if (tag2 != "")
            {
                if (r == Rarity.Star4)
                {
                    string hold = TextBox4StarTags.Text;
                    string temp = hold + $"({tag1}, {tag2}), ";
                    TextBox4StarTags.Text = temp;
                }
                else
                {
                    string hold = TextBox5StarTags.Text;
                    string temp = hold + $"({tag1}, {tag2}), ";
                    TextBox5StarTags.Text = temp;
                }
            }
            else if (tag1 != "")
            {
                if (r == Rarity.Star4)
                {
                    string hold = TextBox4StarTags.Text;
                    string temp = hold + $"({tag1}), ";
                    TextBox4StarTags.Text = temp;
                }
                else
                {
                    string hold = TextBox5StarTags.Text;
                    string temp = hold + $"({tag1}), ";
                    TextBox5StarTags.Text = temp;
                }
            }
        }

        public void ResizeResultBoxFontSize()
        {
            if (Width < 700 && Height < 400)
            {
                TextBox4StarTags.FontSize = 7;
                TextBox5StarTags.FontSize = 7;
            }
            else
            {
                TextBox4StarTags.FontSize = 11;
                TextBox5StarTags.FontSize = 11;
            }
        }

        public void MoveWindow()
        {
            Left = CapturedWindowRect.Left / 1.5;
            Top = CapturedWindowRect.Top / 1.5;

            Width = (CapturedWindowRect.Right - CapturedWindowRect.Left) / 1.5;
            Height = (CapturedWindowRect.Bottom - CapturedWindowRect.Top) / 1.5;

            CanvasBox.Width = Width;
            CanvasBox.Height = Height;
            Canvas.SetTop(TagsAvailable, Height / 1.33);
            Canvas.SetLeft(TagsAvailable, Width / 2.75);
            TagsAvailable.Width = Width / 3.7;
            TagsAvailable.Height = Height / 6;
        }

        // this is high scale number tweaking operation
        public void ResizeTagBoxes()
        {
            MainGridBorderTop.Height = new GridLength(Height * 0.5);
            MainGridBorderBottom.Height = new GridLength(Height * 0.30);

            MainGridBorderLeft.Width = new GridLength(Width * 0.275);
            MainGridBorderRight.Width = new GridLength(Width * 0.34);
        }

        // this too
        public void UpdateTagBoxesPositionData()
        {
            // if just one thing is same position (here first box X coordinate (col1)), then no need for update coz app wasnt resized
            if (TagBoxes[0].X == (float)(CapturedWindowRect.Left + (Width * 0.42)))
            {
                return;
            }

            float row1 = (float)(CapturedWindowRect.Top + (Height * 0.76));
            float row2 = (float)(CapturedWindowRect.Top + (Height * 0.90));
                                 
            float col1 = (float)(CapturedWindowRect.Left + (Width * 0.42));
            float col2 = (float)(CapturedWindowRect.Left + (Width * 0.61));
            float col3 = (float)(CapturedWindowRect.Left + (Width * 0.80));

            TagBoxes = new Vector2[BoxCount]
            {
                new Vector2(col1, row1),
                new Vector2(col2, row1),
                new Vector2(col3, row1),
                new Vector2(col1, row2),
                new Vector2(col2, row2),
            };
        }

        public enum Rarity
        {
            Star4 = 4,
            Star5 = 5,
        }

        private void CloseAppButtonClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}

