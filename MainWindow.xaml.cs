using ImageMagick;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
           { (( "Caster", "Slow", "DPS"), Rarity.Star5) },
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
            //
            string processName = File.ReadAllText($"{AppContext.BaseDirectory}ProcessName.txt").Split(":")[2].Trim();
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                MessageBox.Show("LDPlayer needs to be open for application to work.", "LDPlayer is not open");
                throw new Exception("LDPlayer needs to be open for application to work.");
            }
            Ptr = processes[0].MainWindowHandle;

            // sigh https://github.com/charlesw/tesseract/issues/636#event-1299319774
            TesseractEnviornment.CustomSearchPath = Environment.CurrentDirectory;

            // custom models wont work coz when i try to train tesseract the trained models are always bad
            // might be skill issue but i followed documentation on how to do it so if this is not good enough then goodbye
            Engine = new TesseractEngine($"{TesseractEnviornment.CustomSearchPath}\\tessdata", "eng", EngineMode.Default);

            // accuracy options test
            // single word > single column > single line > raw line
            //  ^ seems best from all the options other ones either dont work or are just worse than best option
            Engine.DefaultPageSegMode = PageSegMode.SingleWord;

            MagickReadSettings.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
            //MorphologySettings = new MorphologySettings()
            //{
            //    Iterations = 1, // 1 was too little and 2 seems perfect
            //    Kernel = Kernel.Diamond,
            //    Method = MorphologyMethod.Erode,
            //};

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
                // old implementation but will leave it here for now coz i already wanted it 3 times to check something
                //Bitmap bitmap = new Bitmap((int)(Width * 0.17), (int)(Height * 0.13));
                //Graphics g = Graphics.FromImage(bitmap);
                //for (int i = 0; i < BoxCount; i++)
                //{
                //    g.CopyFromScreen((int)(TagBoxes[i].X), (int)(TagBoxes[i].Y), 0, 0, bitmap.Size);
                //    bitmap.Save($"banana{i}.png", System.Drawing.Imaging.ImageFormat.Png);
                //}
                //g.Dispose();
                //bitmap.Dispose();
                
                for (int i = 0; i < BoxCount; i++)
                {
                    // original > MagickReadSettings.ExtractArea = new MagickGeometry((int)TagBoxes[i].X, (int)TagBoxes[i].Y, (uint)(Width * 0.17), (uint)(Height * 0.13));
                    MagickReadSettings.ExtractArea = new MagickGeometry(
                        // 10 is some kind of padding Arknights uses i think? helps with screenshot positions to be more centered on tag names
                        (int)TagBoxes[i].X - 10, (int)TagBoxes[i].Y + 10,
                        (uint)(Width * 0.15), (uint)(Height * 0.05));
                    
                    using (MagickImage image = new MagickImage("SCREENSHOT:", MagickReadSettings))
                    {
                        // some options i used but werent really needed but i will leave this commented to know it exists
                        //image.Morphology(MorphologySettings);
                        //image.AdaptiveSharpen(0, 30.0);
                        //image.UnsharpMask(1, 1);
                        //image.Grayscale(); // when box becomes blue (selected) this improves OCR accuracy

                        // maybe dynamically adjust this so image size will always be the same... too big = bad and too small = also bad
                        // 400 seems pretty good there with all other configurations i have set up
                        //image.Scale(new Percentage(100 / (image.Width / 200.0)));
                        
                        // negate and then recolour grayish colours to pure white for best accuracy
                        image.Negate(Channels.RGB);

                        // is this love in the air? no its RAM leak *PC explodes*... would be nice if there was info that this is disposable
                        using (IPixelCollection<byte> pixels = image.GetPixels())
                        {
                            foreach (IPixel<byte> pixel in pixels)
                            {
                                IMagickColor<byte>? currentPixelColour = pixel.ToColor();
                                if (currentPixelColour.R > 130 && currentPixelColour.G > 130 && currentPixelColour.B > 130)
                                {
                                    pixel.SetChannel(0, 255);
                                    pixel.SetChannel(1, 255);
                                    pixel.SetChannel(2, 255);
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
            Console.WriteLine("Time Elapsed: " + w.ElapsedMilliseconds);
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
                // this new line is pure evil
                string pageText = page.GetText().Replace("\n", "");
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
            // leaving this comment coz funny > string OCRTags = Regex.Replace(ExtractedText(), @$"\t|\n|\r|Q|\|;|-|{(char)45}|:|`|'|_|‘|{(char)8212}|I| |", "").Replace(".", "");
            string OCRTags = Regex.Replace(ExtractedText(), "[^a-zA-Z0-9,]", "");

            // for testing
            //string OCRTags = "Defender,Caster,FastRedeploy,AOE,Slow";
            //string OCRTags = "Meleee,PS,FastERedeploy,ABE,Slaow";
            //string OCRTags = "Melee,DPS,FastRedeploy,AOE,Slow";
            //string OCRTags = "DPS,DPRecovery,FastRedeploy,AOE,Slow";
            //string OCRTags = "Support,Supporter,FastRedeploy,AOE,Slow";
            //string OCRTags = "ps,ae,Melee,Support,Slow";

            // only DPS tag is not really correct from what i see... making ToLower everything might fix it
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

                    // all tags start with higher case letters
                    // this deletes lower case first character only if tag contains upper case character for some edge case
                    // with tag errors like "ps" instead of "DPS", where "ps" will still work as a tag but just "s" wont
                    if (tags[i].Any(c => char.IsUpper(c)) && char.IsLower(tags[i][0]))
                    {
                        string newTagName = tags[i].Remove(0, 1);
                        tags[i] = newTagName;
                    }

                    // im fucking stupid end my miserable life THIS IS WHY CASTER DIDNT WORK FFS
                    //if (tags[i][0] == 'C' && tags[i][1] != 'r')
                    // this is one character??? H O W ??? sure i guess you learn something new everyday
                    //if (tags[i][0] == 'ﬁ')
                }
                catch { } // just so app doesnt crash and continues working in rare situations
            }

            #if DEBUG
            // clear console coz i like it that way
            Console.Clear();
            // windows 11 is so dogshit that clearing console doesnt work and you need to also put this magic runes for it to work (https://stackoverflow.com/questions/75471607/console-clear-doesnt-clean-up-the-whole-console)
            Console.WriteLine("\x1b[3J");

            Console.WriteLine("NOTE: tags can have 1 character error in them to work correctly.");
            for (int i = 0; i < tags.Length; i++)
            {
                Console.WriteLine($"Tag{i + 1}: " + tags[i]);
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

        // one last small error here to fix
        public bool IsSame(string tag1, string tag2)
        {
            // return early if character difference is higher than 1
            if (Math.Abs(tag1.Length - tag2.Length) > 1)
            {
                return false;
            }

            // sometimes there might be error where for example DPS is read as Dps, ps, DPs etc.
            tag1 = tag1.ToLower();
            tag2 = tag2.ToLower();

            int matchedChars = 0;
            int wrongCharCount = 0;

            int appendIndex = 0;
            int j = 0;
            int i = 0;
            while (true)
            {
                j = i + appendIndex;
                if (i + 1 == tag1.Length && j + appendIndex < tag2.Length)
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
                if (i + 1 >= tag1.Length && j + appendIndex >= tag2.Length)
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
                    AddTo(TextBox4StarTags, $"({tag1}, {tag2}, {tag3}), ");
                }
                else
                {
                    AddTo(TextBox5StarTags, $"({tag1}, {tag2}, {tag3}), ");

                }
            }
            else if (tag2 != "")
            {
                if (r == Rarity.Star4)
                {
                    AddTo(TextBox4StarTags, $"({tag1}, {tag2}), ");

                }
                else
                {
                    AddTo(TextBox5StarTags, $"({tag1}, {tag2}), ");

                }
            }
            else if (tag1 != "")
            {
                if (r == Rarity.Star4)
                {
                    AddTo(TextBox4StarTags, $"({tag1}), ");
                }
                else
                {
                    AddTo(TextBox5StarTags, $"({tag1}), ");
                }
            }

            void AddTo(TextBlock textBlock, string tags)
            {
                if (textBlock.Text.Contains(tags))
                {
                    return;
                }

                textBlock.Text = textBlock.Text + tags;
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

            // window that stalks tag box wont delete coz math is hard
            //borderr.Height = Height * 0.05;
            //borderr.Width = Width * 0.105;
            //Canvas.SetLeft(borderr, (Width * 0.297) - 10); // 10 here is i guess padding in arknights
            //Canvas.SetTop(borderr,  (Height * 0.505) + 10);

            float row1 = (float)(CapturedWindowRect.Top + (Height * 0.788));
            float row2 = (float)(CapturedWindowRect.Top + (Height * 0.930));
                                 
            float col1 = (float)(CapturedWindowRect.Left + (Width * 0.45));
            float col2 = (float)(CapturedWindowRect.Left + (Width * 0.64));
            float col3 = (float)(CapturedWindowRect.Left + (Width * 0.83));

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

