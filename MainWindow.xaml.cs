using System.Diagnostics;
using System.Drawing;
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

        // all of these here are just so its easy to access
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

        public MainWindow()
        {
            InitializeComponent();

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(200);
            timer.Tick += Update;

            Process[] processes;
            do
            {   // what program window should follow here
                processes = Process.GetProcessesByName("dnplayer");
            }
            while (processes.Length == 0);

            Ptr = processes[0].MainWindowHandle;

            while (Ptr == 0x0000000000000000)
            {
                Ptr = processes[0].MainWindowHandle;
            }

            // sigh https://github.com/charlesw/tesseract/issues/636#event-1299319774
            TesseractEnviornment.CustomSearchPath = Environment.CurrentDirectory;

            timer.Start();
        }

        private void Update(object? sender, EventArgs e)
        {
            GetWindowRect(Ptr, ref CapturedWindowRect);
            MoveWindow();
            ResizeTagBoxes();

            Vector2[] boxes = CreateTagBoxes();

            try
            {
                Bitmap bitmap = new Bitmap((int)(Width * 0.17), (int)(Height * 0.13));
                Graphics g = Graphics.FromImage(bitmap);
                for (int i = 0; i < boxes.Length; i++)
                {
                    Vector2 box = boxes[i];
                    g.CopyFromScreen((int)(box.X), (int)(box.Y), 0, 0, bitmap.Size);
                    bitmap.Save($"banana{i}.png", System.Drawing.Imaging.ImageFormat.Png);
                }
                g.Dispose();
                bitmap.Dispose();

                MarkTag();
            } catch { } // so app doesnt crashh when app in launched but ldplayer is not
           
            // it basically doesnt affect performance at all but cuts off RAM jumping from 100MB to ~300MB+ and then clears,
            // to stable <100MB RAM consumption and there is nothing in memory that needs to be saved anyway
            // if there are problems just comment this tho i didnt see any
            GC.Collect();
        }

        public string ExtractedText()
        {
            string text = "";
    
            TesseractEngine engine = new TesseractEngine($"{TesseractEnviornment.CustomSearchPath}\\tessdata", "eng", EngineMode.Default);
            engine.DefaultPageSegMode = PageSegMode.RawLine;

            for (int i = 0; i < 6; i++)
            {
                Pix img = Pix.LoadFromFile($"banana{i}.png");
                Pix scaledImage = img.Scale(2, 2);

                // doesnt really help but leaving it here to know this even exists
                //Pix grayImage = scaledImage.ConvertRGBToGray();
                //Pix thresholdedPix = grayImage.BinarizeOtsuAdaptiveThreshold(16, 16, 0, 0, 1.0f);

                Tesseract.Page page = engine.Process(scaledImage);
                string pageText = page.GetText();
                if (pageText != "")
                {
                    if (i != 5)
                    {
                        string hold = text;
                        string temp = hold + pageText + ", ";
                        text = temp;
                    }
                }

                page.Dispose();
                img.Dispose();
            }
            engine.Dispose();

            return text;
        }

        public void MarkTag()
        {
            // regex to reduce random noise characters that appear                               im sorry but W H Y???   i dont care IT WORKS and .| doesnt
            string OCRTags = Regex.Replace(ExtractedText(), @$"\t|\n|\r|Q|\|;|-|{(char)45}|:|`|'|_|‘|{(char)8212}", "").Replace(".", "");
            
            // for testing
            //Melee, DPS, FastRedeploy, AOE, Slow, 
            //string OCRTags = "Shift, DPS, FastRedeploy, AOE, Slow, ";

            string[] tags = OCRTags.Split(", ");

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
                    if (tags[i][0] == 'r' || tags[i][0] == 'C' && tags[i][1] != 'r')
                    {
                        string newTagName = tags[i].Remove(0, 1);
                        tags[i] = newTagName;
                    }

                    // this is one character??? H O W ??? sure i guess you learn something new everyday
                    if (tags[i][0] == 'ﬁ')
                    {
                        string newTagName = tags[i].Remove(0, 1);
                        tags[i] = newTagName;
                    }
                }
                catch { } // just so app doesnt crash and continues working in rare situations
            }

            (string, Rarity)[] combos = null!;
            // so many loops!
            // i bet there are better ways to do this but im lazy and only like 1 list has 8 items with average of like 5 or less
            // in worst case scenario it takes <1ms so if someone has a problem i have lego you can step on
            for (int i = 0; i < tags.Length - 1; i++)
            {
                if (Solo4StarTags.Contains(tags[i]))
                {
                    AddTextComboToBox(Rarity.Star4, tags, i);
                }

                if (Tag2Combos.ContainsKey(tags[i]))
                {
                    combos = Tag2Combos[tags[i]];
                }

                for (int j = 0; j < tags.Length - 1; j++)
                {
                    if (combos != null)
                    {
                        foreach ((string tag, Rarity r) s in combos)
                        {
                            if (tags[i] != tags[j] && s.tag == tags[j])
                            {
                                AddTextComboToBox(s.r, tags, i, j);
                            }
                        }
                    }

                    // for EXTREMELY rare 3 tag combo
                    for (int k = 0; k < tags.Length - 1; k++)
                    {
                        foreach (((string tag1, string tag2, string tag3), Rarity r) s in Tag3Combos)
                        {
                            if (s.Item1.tag1 == tags[i] && s.Item1.tag2 == tags[j] && s.Item1.tag3 == tags[k])
                            {
                                AddTextComboToBox(s.r, tags, i, j, k);
                            }
                        }
                    }
                }

                combos = null!;
            }
        }

        public void AddTextComboToBox(Rarity r, string[] tags, int i = -1, int j = -1, int k = -1)
        {
            if (i != -1 && j != -1 && k != -1)
            {
                if (r == Rarity.Star4)
                {
                    string hold = TextBox4StarTags.Text;
                    string temp = hold + $"({tags[i]}, {tags[j]}, {tags[k]})";
                    TextBox4StarTags.Text = temp;
                }
                else
                {
                    string hold = TextBox5StarTags.Text;
                    string temp = hold + $"({tags[i]}, {tags[j]}, {tags[k]})";
                    TextBox5StarTags.Text = temp;
                }
            }
            else if (i != -1 && j != -1)
            {
                if (r == Rarity.Star4)
                {
                    string hold = TextBox4StarTags.Text;
                    string temp = hold + $"({tags[i]}, {tags[j]}), ";
                    TextBox4StarTags.Text = temp;
                }
                else
                {
                    string hold = TextBox5StarTags.Text;
                    string temp = hold + $"({tags[i]}, {tags[j]}), ";
                    TextBox5StarTags.Text = temp;
                }
            }
            else if (i != -1)
            {
                if (r == Rarity.Star4)
                {
                    string hold = TextBox4StarTags.Text;
                    string temp = hold + $"({tags[i]}), ";
                    TextBox4StarTags.Text = temp;
                }
                else
                {
                    string hold = TextBox5StarTags.Text;
                    string temp = hold + $"({tags[i]}), ";
                    TextBox5StarTags.Text = temp;
                }
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
        public Vector2[] CreateTagBoxes()
        {
            float row1 = (float)(CapturedWindowRect.Top + (Height * 0.76));
            float row2 = (float)(CapturedWindowRect.Top + (Height * 0.90));
                                 
            float col1 = (float)(CapturedWindowRect.Left + (Width * 0.42));
            float col2 = (float)(CapturedWindowRect.Left + (Width * 0.61));
            float col3 = (float)(CapturedWindowRect.Left + (Width * 0.80));

            Vector2[] boxes =
            {
                new Vector2(col1, row1),
                new Vector2(col2, row1),
                new Vector2(col3, row1),
                new Vector2(col1, row2),
                new Vector2(col2, row2),
                new Vector2(col3, row2),
            };

            return boxes;
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

