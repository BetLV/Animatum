﻿using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Animatum.Updater.Controls
{
    // The frame type to draw in the paint events.
    public enum FrameType { WelcomeFinish, TextInfo, Update }

    public enum ImageAlign { Left = 0, Right = 1, Fill = 2 }

    public enum UpdateItemStatus { Error = -1, Nothing = 0, Working = 1, Success = 2 }

    internal class PanelDisplay : ContainerControl
    {
        #region Private Variables

        public ImageAlign HeaderImageAlign = ImageAlign.Left;
        public int HeaderIndent = 14;
        public Color HeaderTextColor = Color.Black;

        string m_Description;
        Rectangle m_DescriptionRect;

        string m_Body;
        Rectangle m_BodyRect;

        string m_BottomText;
        Rectangle m_BottomRect;

        public string ErrorDetails;

        //message (and/or license)
        readonly RichTextBoxEx messageBox = new RichTextBoxEx();

        //downloading and installing
        readonly Windows7ProgressBar progressBar = new Windows7ProgressBar();
        int m_Progress;

        string m_ProgressStatus;
        Rectangle m_ProgressStatusRect;

        //padding for the text
        const int m_LeftPad = 14;
        const int m_RightPad = 14;
        const int m_TopPad = 14;

        //offset for the Top description
        const int m_DescriptionOffset = 10;

        //the total HeaderHeight (including 3d line)
        const int m_HeaderHeight = 59;

        public FrameType TypeofFrame;

        //"working" animation
        readonly AnimationControl aniWorking;

        LinkLabel2 noUpdateAvailableLink;
        string noUpdateAvailableURL;

        Button errorDetailsButton;

        #endregion Private Variables

        //Update Items
        public bool ShowChecklist;
        public UpdateItem[] UpdateItems = new UpdateItem[4];

        #region Properties

        /// <summary>
        /// The progress of the install or download.
        /// </summary>
        public int Progress
        {
            set
            {
                m_Progress = value;
                if (progressBar != null)
                {
                    if (m_Progress > 100)
                        m_Progress = 100;
                    else if (m_Progress < 0)
                        m_Progress = 0;

                    progressBar.Value = m_Progress;
                }
            }
        }

        public void PauseProgressBar()
        {
            progressBar.State = ProgressBarState.Pause;
        }

        public void UnPauseProgressBar()
        {
            progressBar.State = ProgressBarState.Normal;
        }

        public void AppendText(string plaintext)
        {
            // we can't set SelectedText with an empty string
            // or it makes a beep noise (thanks Microsoft).
            if (string.IsNullOrEmpty(plaintext))
                return;

            messageBox.Select(messageBox.Text.Length, 0);
            messageBox.SelectedText = plaintext;
        }

        public void AppendRichText(string rtfText)
        {
            messageBox.Select(messageBox.Text.Length, 0);
            messageBox.SelectedRtf = messageBox.SterilizeRTF(rtfText);
        }

        public void AppendAndBoldText(string plaintext)
        {
            // set the cursor to the end of the file
            messageBox.Select(messageBox.Text.Length, 0);

            // store the current font, and change to bold
            Font prevSelectionFont = messageBox.SelectionFont;
            messageBox.SelectionFont = new Font(messageBox.SelectionFont, FontStyle.Bold);

            // append the text
            messageBox.SelectedText = plaintext;

            //revert the selection font back to the old one
            messageBox.SelectionFont = prevSelectionFont;
        }

        public void ClearText()
        {
            messageBox.Clear();
        }

        public string GetChanges(bool rtf)
        {
            return rtf ? messageBox.Rtf : messageBox.Text;
        }

        /// <summary>
        /// The status shown right below the progress bar.
        /// </summary>
        public string ProgressStatus
        {
            get { return m_ProgressStatus; }
            set
            {
                m_ProgressStatus = value;
                if (progressBar != null && progressBar.Visible)
                {
                    m_ProgressStatusRect = UpdateTextSize(m_ProgressStatus,
                        new Padding(m_LeftPad, progressBar.Bottom + 4, m_RightPad, 0),
                        TextFormatFlags.SingleLine | TextFormatFlags.WordEllipsis, Font);

                    //Invalidate the appropriate region
                    Invalidate(new Rectangle(m_LeftPad, progressBar.Bottom, Width - m_LeftPad - m_RightPad, Height - progressBar.Bottom));
                }
            }
        }

        #endregion

        #region Constructors

        public PanelDisplay(int width, int height)
        {
            Width = width;
            Height = height;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Opaque, true);

            //setup the progressbar
            progressBar.Location = new Point(m_LeftPad, Height - 60);
            progressBar.Size = new Size(Width - m_LeftPad - m_RightPad, 20);
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Visible = false;
            Controls.Add(progressBar);

            //setup the messageBox
            messageBox.Location = new Point(m_LeftPad, m_HeaderHeight + m_TopPad + 20);
            messageBox.Multiline = true;
            messageBox.Size = new Size(Width - m_LeftPad - m_RightPad, 0);
            messageBox.ReadOnly = true;
            messageBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            messageBox.BackColor = SystemColors.Window; //white
            messageBox.Visible = false;
            Controls.Add(messageBox);

            //setup the animation list
            for (int i = 0; i < UpdateItems.Length; i++)
            {
                UpdateItems[i] = new UpdateItem
                {
                    AnimationWidth = 16,
                    Visible = false,
                    Left = 45
                };
                Controls.Add(UpdateItems[i].Animation);
                Controls.Add(UpdateItems[i].Label);
            }

            // the single centered animation
            aniWorking = new AnimationControl
            {
                Columns = 18,
                Rows = 1,
                AnimationInterval = 46,
                Visible = false,
                Location = new Point((Width / 2), (Height / 2)),
                StaticImage = false,
                BaseImage = UpdateItem.ProgressImage
            };

            Controls.Add(aniWorking);
        }

        #endregion Constructors

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int SendMessage(IntPtr hWnd, UInt32 Msg, int wParam, int lParam);

        public void ChangePanel(FrameType panType, string description, string bottom)
        {
            //WM_SETREDRAW = 0xB;
            SendMessage(Handle, 0xB, 0, 0); //disable drawing

            TypeofFrame = panType;

            m_Description = description;
            m_Body = description;
            m_BottomText = bottom;

            if (panType == FrameType.WelcomeFinish)
            {
                if (ErrorDetails == null)
                    messageBox.Hide();
                else
                {
                    messageBox.Clear();
                    AppendText(ErrorDetails);
                }

                progressBar.Hide();
                HideAnimations();
            }

            //calculate the text sizes and positions
            UpdateTextRectangles();

            progressBar.ShowInTaskbar = false;

            //handle specifics
            switch (panType)
            {
                case FrameType.TextInfo:

                    messageBox.Show();
                    progressBar.Hide();
                    HideAnimations();
                    break;
                case FrameType.Update:

                    messageBox.Hide();
                    progressBar.Show();

                    progressBar.ContainerControl = (Form)TopLevelControl;
                    progressBar.ShowInTaskbar = ShowChecklist;

                    if (ShowChecklist)
                    {
                        // hide center animation
                        if (aniWorking.Visible)
                        {
                            aniWorking.Hide();
                            aniWorking.StopAnimation();
                        }

                        //set the defaults for the UpdateItems
                        for (int i = 0; i < UpdateItems.Length; i++)
                        {
                            //clear any previous state, and show
                            UpdateItems[i].Clear();
                            UpdateItems[i].Show();
                        }
                    }
                    else
                    {
                        //show a single centered animation
                        aniWorking.StartAnimation();
                        aniWorking.Show();

                        progressBar.Hide();
                    }
                    break;
            }

            // re-enable drawing and Refresh
            SendMessage(Handle, 0xB, 1, 0);
            Refresh();
        }

        public void SetNoUpdateAvailableLink(string text, string link)
        {
            noUpdateAvailableLink = new LinkLabel2
            {
                Text = text,
                Visible = false,
                BackColor = Color.White
            };
            noUpdateAvailableLink.Click += NoUpdateAvailableLink_Click;
            noUpdateAvailableURL = link;

            Controls.Add(noUpdateAvailableLink);
        }

        void NoUpdateAvailableLink_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(noUpdateAvailableURL);
        }

        public void SetUpErrorDetails(string detailsButton)
        {
            errorDetailsButton = new Button
            {
                Text = detailsButton,
                FlatStyle = FlatStyle.System,
                Visible = false,
                AutoSize = true,
                Padding = new Padding(6, 0, 6, 0)
            };

            errorDetailsButton.Click += errorDetailsButton_Click;

            Controls.Add(errorDetailsButton);
        }

        void errorDetailsButton_Click(object sender, EventArgs e)
        {
            errorDetailsButton.Visible = false;
            messageBox.Show();
        }

        void UpdateTextRectangles()
        {
            //calculate description rectangle
            m_DescriptionRect = UpdateTextSize(m_Description,
                new Padding(m_LeftPad, m_TopPad, m_RightPad, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoClipping, Font);

            //calculate bottom rectangle
            m_BottomRect = UpdateTextSize(m_BottomText,
                new Padding(m_LeftPad, 0, m_RightPad, m_TopPad + 9),
                TextFormatFlags.WordBreak, Font, ContentAlignment.BottomRight);

            if (TypeofFrame != FrameType.WelcomeFinish)
            {
                //calculate body rectangle
                m_BodyRect = UpdateTextSize(m_Body,
                    new Padding(m_LeftPad, m_TopPad, m_RightPad, 0),
                    TextFormatFlags.WordBreak, Font);

                //Resize the messageBox
                if (TypeofFrame == FrameType.TextInfo)
                {
                    messageBox.Top = m_BodyRect.Bottom + 5;
                    messageBox.Height = Height - messageBox.Top - 5 - (Bottom - m_BottomRect.Top);
                }

                //Reposition the m_UpdateItems
                if (ShowChecklist)
                {
                    for (int i = 0; i < UpdateItems.Length; i++)
                    {
                        UpdateItems[i].Top = m_BodyRect.Bottom + 25 + (30 * i);
                    }
                }
            }
            else if (noUpdateAvailableLink != null) // AND m_TypeOfFrame == WelcomeFinish
            {
                noUpdateAvailableLink.Location = new Point(m_DescriptionRect.Left, m_DescriptionRect.Bottom + 20);
                noUpdateAvailableLink.Visible = true;
            }
            else if (ErrorDetails != null) // m_TypeofFrame == FrameType.WelcomeFinish
            {
                errorDetailsButton.Location = new Point(Width - m_RightPad - errorDetailsButton.Width, m_DescriptionRect.Bottom + 5);
                errorDetailsButton.Visible = true;

                //Resize the messageBox
                messageBox.Location = new Point(m_LeftPad, m_DescriptionRect.Bottom + 5);
                messageBox.Size = new Size(Width - m_LeftPad - m_RightPad,
                           Height - messageBox.Top - 5 - (Bottom - m_BottomRect.Top));
            }
        }

        Rectangle UpdateTextSize(string text, Padding padding, TextFormatFlags flags, Font font)
        {
            return UpdateTextSize(text, padding, flags, font, ContentAlignment.TopLeft);
        }

        Rectangle UpdateTextSize(string text, Padding padding, TextFormatFlags flags, Font font, ContentAlignment alignment)
        {
            if (font == null)
                font = Font;

            Size tabTextSize = TextRenderer.MeasureText(text,
                font,
                new Size(Width - padding.Left - padding.Right, 1),
                flags | TextFormatFlags.NoPrefix);

            if (alignment == ContentAlignment.BottomRight)
                return new Rectangle(new Point(Width - tabTextSize.Width - padding.Right, Height - tabTextSize.Height - padding.Bottom), tabTextSize);

            return new Rectangle(new Point(padding.Left, padding.Top), tabTextSize);
        }

        void HideAnimations()
        {
            //set the defaults for the UpdateItems
            for (int i = 0; i < UpdateItems.Length; i++)
            {
                UpdateItems[i].Hide();
            }
            aniWorking.Hide();
            aniWorking.StopAnimation();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
            e.Graphics.InterpolationMode = InterpolationMode.Low;
            
            //background 
            e.Graphics.FillRectangle(SystemBrushes.Control, 0, 0, Width, Height);

            DrawMain(e.Graphics);
        }

        void DrawMain(Graphics gr)
        {
            if (TypeofFrame == FrameType.WelcomeFinish)
            {
                //Draw m_Description
                TextRenderer.DrawText(gr, m_Description, Font, m_DescriptionRect, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
            else
            {
                //Draw m_Body
                TextRenderer.DrawText(gr, m_Body, Font, m_BodyRect, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            //Draw m_BottomText
            if (!string.IsNullOrEmpty(m_BottomText))
                TextRenderer.DrawText(gr, m_BottomText, Font, m_BottomRect, ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);


            //Draw m_ProgressStatus
            if (!string.IsNullOrEmpty(m_ProgressStatus) && progressBar.Visible)
                TextRenderer.DrawText(gr, m_ProgressStatus, Font,
                    m_ProgressStatusRect, ForeColor,
                    TextFormatFlags.SingleLine | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPrefix);


            // draw bottom divider & branding
            int brandingWidth = DrawBranding(gr, 3, ClientRectangle.Bottom - m_TopPad);

            Draw3DLine(gr, brandingWidth, Width, ClientRectangle.Bottom - m_TopPad);
        }

        protected static void Draw3DLine(Graphics gr, int x1, int x2, int y1)
        {
            gr.DrawLine(SystemPens.ControlDark, x1, y1, x2, y1);
            gr.DrawLine(SystemPens.ControlLightLight, x1, y1 + 1, x2, y1 + 1);
        }

        int DrawBranding(Graphics gr, int x, int midPointY)
        {
            SizeF textSize = gr.MeasureString("Animatum Updater", Font);
            midPointY -= (int)(textSize.Height / 2.0);

            //draw the text
            gr.DrawString("Animatum Updater", Font, SystemBrushes.ControlLightLight, new PointF(x, midPointY));
            gr.DrawString("Animatum Updater", Font, SystemBrushes.ControlDark, new PointF(x - 1, midPointY - 1));

            return (int)textSize.Width + x;
        }
    }

    internal class UpdateItem
    {
        static readonly Image ErrorImage = ManifestResourceLoader.LoadBitmap("Resources/cross.png");
        static readonly Image SuccessImage = ManifestResourceLoader.LoadBitmap("Resources/tick.png");
        public static readonly Image ProgressImage = ManifestResourceLoader.LoadBitmap("Resources/loading-blue.png");

        public AnimationControl Animation = new AnimationControl();
        public Label Label = new Label { AutoSize = true };

        public int AnimationWidth { get; set; }

        UpdateItemStatus m_Status;

        //Position
        int m_Left, m_Top;

        public int Left
        {
            get { return m_Left; }
            set
            {
                m_Left = value;
                Animation.Left = m_Left;
                Label.Left = m_Left + AnimationWidth + 5;
            }
        }

        public int Top
        {
            get { return m_Top; }
            set
            {
                m_Top = value;
                Animation.Top = m_Top;
                Label.Top = m_Top;
            }
        }

        public string Text
        {
            get { return Label.Text; }
            set { Label.Text = value; }
        }

        public bool Visible
        {
            get
            {
                return Animation.Visible;
            }
            set
            {
                Animation.Visible = value;
                Label.Visible = value;
            }
        }

        public void Show()
        {
            Visible = true;
        }

        public void Hide()
        {
            Visible = false;
        }

        public void Clear()
        {
            m_Status = UpdateItemStatus.Nothing;
            Label.Text = String.Empty;
        }

        public UpdateItemStatus Status
        {
            get { return m_Status; }
            set
            {
                // only set m_Status if it's a different status
                if (m_Status == value)
                    return;

                m_Status = value;

                switch (m_Status)
                {
                    case UpdateItemStatus.Error:
                        Animation.StopAnimation();
                        Animation.StaticImage = true;
                        Animation.Rows = 4;
                        Animation.Columns = 8;
                        Animation.AnimationInterval = 25;
                        Animation.BaseImage = ErrorImage;
                        Animation.StartAnimation();
                        Label.Font = new Font(Label.Font, FontStyle.Regular);
                        break;
                    case UpdateItemStatus.Nothing:
                        Animation.BaseImage = null;
                        Animation.StartAnimation();
                        break;
                    case UpdateItemStatus.Working:
                        Animation.StopAnimation();
                        Animation.StaticImage = false;
                        Animation.Rows = 1;
                        Animation.Columns = 18;
                        Animation.AnimationInterval = 46;
                        Animation.BaseImage = ProgressImage;
                        Animation.StartAnimation();
                        Label.Font = new Font(Label.Font, FontStyle.Bold);
                        break;
                    case UpdateItemStatus.Success:
                        Animation.StopAnimation();
                        Animation.StaticImage = true;
                        Animation.Rows = 4;
                        Animation.Columns = 8;
                        Animation.AnimationInterval = 25;
                        Animation.BaseImage = SuccessImage;
                        Animation.StartAnimation();
                        Label.Font = new Font(Label.Font, FontStyle.Regular);
                        break;
                }
            }
        }
    }
}
