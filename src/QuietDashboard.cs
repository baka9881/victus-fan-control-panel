using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VictusQuiet
{
    sealed class DashboardButton : Button
    {
        bool hovered, pressed;
        public DashboardButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); }
        protected override void OnMouseEnter(EventArgs args){hovered=true;Invalidate();base.OnMouseEnter(args);}
        protected override void OnMouseLeave(EventArgs args){hovered=pressed=false;Invalidate();base.OnMouseLeave(args);}
        protected override void OnMouseDown(MouseEventArgs args){pressed=true;Invalidate();base.OnMouseDown(args);}
        protected override void OnMouseUp(MouseEventArgs args){pressed=false;Invalidate();base.OnMouseUp(args);}
        protected override void OnPaint(PaintEventArgs args)
        {
            Color background=Enabled?BackColor:Color.FromArgb(23,33,46);
            if(Enabled && hovered)background=ControlPaint.Light(background,pressed?.04f:.12f);
            using(Brush fill=new SolidBrush(background))args.Graphics.FillRectangle(fill,ClientRectangle);
            using(Pen border=new Pen(Enabled?FlatAppearance.BorderColor:Color.FromArgb(43,57,75)))args.Graphics.DrawRectangle(border,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
            Rectangle text=Rectangle.FromLTRB(Padding.Left+7,2,Width-Padding.Right-7,Height-2);
            TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis;
            flags|=TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(args.Graphics,Text,Font,text,Enabled?ForeColor:Color.FromArgb(117,138,160),flags);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(args.Graphics,new Rectangle(4,4,Math.Max(0,Width-9),Math.Max(0,Height-9)));
        }
    }
    sealed class SessionProgress : Control
    {
        int value;
        public int Value { get{return value;}set{this.value=Math.Max(0,Math.Min(300,value));Invalidate();} }
        public SessionProgress(){DoubleBuffered=true;}
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(Color.FromArgb(38,53,70));
            using(Brush fill=new SolidBrush(Color.FromArgb(74,215,196)))args.Graphics.FillRectangle(fill,0,0,Width*value/300f,Height);
        }
    }
    sealed class FanHistory : Control
    {
        sealed class PointSample { public DateTime At; public int First, Second; }
        readonly List<PointSample> samples = new List<PointSample>();
        public FanHistory() { DoubleBuffered = true; BackColor = Color.FromArgb(24,34,47); }
        public void Add(DateTime at, int first, int second)
        {
            if (samples.Count > 0 && (at - samples[samples.Count-1].At).TotalSeconds < 0.8) return;
            samples.Add(new PointSample { At = at, First = first, Second = second });
            while (samples.Count > 0 && (at - samples[0].At).TotalSeconds > 122) samples.RemoveAt(0);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            Graphics g = args.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle plot = new Rectangle(48, 47, Math.Max(10,Width-68), Math.Max(20,Height-77));
            int ceiling = 4000;
            foreach (PointSample sample in samples) ceiling = Math.Max(ceiling, ((Math.Max(sample.First,sample.Second)+999)/1000)*1000);
            using (Pen grid = new Pen(Color.FromArgb(48,62,78)))
            using (Brush text = new SolidBrush(Color.FromArgb(144,165,187)))
            using (Font small = new Font("Microsoft JhengHei UI", 9))
            {
                g.DrawString("轉速變化  ·  最近 2 分鐘", small, text, 16, 14);
                g.DrawString("風扇 1", small, Brushes.Turquoise, Math.Max(220, Width-180), 14);
                g.DrawString("風扇 2", small, Brushes.MediumPurple, Math.Max(292, Width-93), 14);
                for (int i=0; i<=4; i++)
                {
                    float y = plot.Top + plot.Height * i / 4f;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    g.DrawString((ceiling*(4-i)/4/1000.0).ToString("0.#")+"k", small, text, 9, y-8);
                }
                g.DrawString("2 分鐘前", small, text, plot.Left, plot.Bottom+7);
                g.DrawString("現在", small, text, plot.Right-34, plot.Bottom+7);
                if (samples.Count < 2)
                    g.DrawString("等待足夠的即時讀值…", small, text, plot.Left+20, plot.Top+plot.Height/2);
            }
            if (samples.Count < 2) return;
            DateTime now = samples[samples.Count-1].At;
            PointF[] first = new PointF[samples.Count], second = new PointF[samples.Count];
            for (int i=0; i<samples.Count; i++)
            {
                float x = plot.Right - (float)(now-samples[i].At).TotalSeconds/120f*plot.Width;
                first[i] = new PointF(Math.Max(plot.Left,x), plot.Bottom-samples[i].First/(float)ceiling*plot.Height);
                second[i] = new PointF(Math.Max(plot.Left,x), plot.Bottom-samples[i].Second/(float)ceiling*plot.Height);
            }
            using (Pen one = new Pen(Color.FromArgb(74,215,196),2.2f))
            using (Pen two = new Pen(Color.FromArgb(169,143,247),2.2f))
            { g.DrawLines(one,first); g.DrawLines(two,second); }
        }
    }

    sealed class QuietWindow : Form
    {
        static readonly Color Canvas = Color.FromArgb(13,20,30), Surface = Color.FromArgb(24,34,47), Muted = Color.FromArgb(150,170,192), Ink = Color.FromArgb(232,239,248), Accent = Color.FromArgb(74,215,196);
        readonly bool preview;
        readonly string previewScene;
        readonly Panel[] pages = new Panel[3];
        readonly Button[] navigation = new Button[3];
        readonly List<Button> quietButtons = new List<Button>(), autoButtons = new List<Button>();
        readonly List<string> events = new List<string>();
        readonly Label title, subtitle, connection, fan1, fan2, zone, gpu, load, currentMode, countdown, controlMode, controlTimer, status, refreshed;
        readonly Button admin;
        readonly SessionProgress sessionProgress;
        readonly FanHistory history;
        readonly TextBox eventLog;
        readonly System.Windows.Forms.Timer timer;
        HpQuietDevice device;
        ProcessRecoveryGuard guard;
        QuietSession session;
        bool busy, allowClose, closePending;
        string error = "", loggedStatus = "";

        public QuietWindow(bool preview, string scene = "quiet")
        {
            this.preview = preview; previewScene = scene;
            Text = "Victus 風扇控制面板"; Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); ClientSize = new Size(1080,690); MinimumSize = new Size(940,670);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft JhengHei UI",10); BackColor = Canvas; ForeColor = Ink;
            Panel side = new Panel { Dock = DockStyle.Left, Width = 178, BackColor = Color.FromArgb(18,28,41) };
            side.Controls.Add(LabelAt("VICTUS",22,28,145,36,21,Ink));
            side.Controls.Add(LabelAt("風扇控制台",24,69,140,25,10,Muted));
            string[] names = { "01  即時總覽", "02  風扇控制", "03  保護與紀錄" };
            for (int i=0; i<3; i++)
            {
                int page = i;
                Button button = FlatButton(names[i], false); button.Location = new Point(12,135+i*57); button.Size = new Size(154,46);
                button.TextAlign = ContentAlignment.MiddleLeft; button.Padding = new Padding(10,0,0,0);
                button.Click += delegate { ShowPage(page); }; navigation[i]=button; side.Controls.Add(button);
            }
            Label identity = LabelAt("本機控制\r\nHP 88F8 · BIOS F.25\r\n每 2 秒更新讀值",22,0,150,76,9,Muted);
            identity.Dock = DockStyle.Bottom; identity.Padding=new Padding(20,0,0,0); side.Controls.Add(identity);
            Controls.Add(side);
            Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24,20,24,14) };
            Controls.Add(main); main.BringToFront();
            Panel header = new Panel { Dock = DockStyle.Top, Height = 82 };
            title = LabelAt("即時總覽",0,0,500,38,23,Ink);
            subtitle = LabelAt("查看即時狀態，快速切換已驗證的安靜模式。",2,47,530,26,10,Muted);
            connection = LabelAt("尚未取得控制權限",0,0,240,24,9,Muted); connection.TextAlign = ContentAlignment.MiddleRight;
            admin = FlatButton("取得管理員權限",false); admin.Size = new Size(180,34);
            header.Controls.AddRange(new Control[] { title,subtitle,connection,admin });
            header.Resize += delegate { connection.Location = new Point(header.Width-244,2); admin.Location = new Point(header.Width-180,34); };
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 54 };
            status = LabelAt("開啟面板不會直接改變風扇。",0,7,800,44,9,Muted); status.Dock = DockStyle.Fill; footer.Controls.Add(status);
            Panel host = new Panel { Dock = DockStyle.Fill };
            main.Controls.Add(host); main.Controls.Add(footer); main.Controls.Add(header);
            for(int i=0;i<3;i++) { pages[i]=new Panel { Dock=DockStyle.Fill }; host.Controls.Add(pages[i]); }

            // Overview: live metric cards, history and quick controls.
            TableLayoutPanel cards = Grid(4); cards.Dock=DockStyle.Top; cards.Height=118;
            fan1=Metric(cards,0,"風扇 1","RPM · 估計",Accent);
            fan2=Metric(cards,1,"風扇 2","RPM · 估計",Color.FromArgb(169,143,247));
            zone=Metric(cards,2,"ACPI 熱區","°C · 非核心溫度",Ink);
            gpu=Metric(cards,3,"GPU 溫度","°C · NVIDIA",Ink);
            Panel summary = new Panel { Dock=DockStyle.Top, Height=55 };
            load=LabelAt("CPU 負載 —　 GPU 負載 —",2,18,470,28,10,Muted);
            refreshed=LabelAt("等待連線",0,18,200,28,9,Muted); refreshed.TextAlign=ContentAlignment.MiddleRight;
            summary.Controls.AddRange(new Control[]{load,refreshed}); summary.Resize+=delegate{refreshed.Location=new Point(summary.Width-200,18);};
            Panel actions = new Panel { Dock=DockStyle.Bottom, Height=128, Padding=new Padding(0,12,0,0) };
            currentMode=LabelAt("目前：原有管理方式",2,12,480,28,11,Ink);
            countdown=LabelAt("每次低速最多 5 分鐘",0,12,260,28,9,Muted); countdown.TextAlign=ContentAlignment.MiddleRight;
            actions.Controls.AddRange(new Control[]{currentMode,countdown});
            TableLayoutPanel quick = Grid(2); quick.Dock=DockStyle.Bottom; quick.Height=67;
            quick.Controls.Add(QuietButton("啟用安靜模式 · 約 1800 RPM"),0,0);
            quick.Controls.Add(AutoButton(),1,0); actions.Controls.Add(quick);
            actions.Resize+=delegate{countdown.Location=new Point(actions.Width-260,12);};
            history=new FanHistory { Dock=DockStyle.Fill, Margin=new Padding(0,0,0,4) };
            pages[0].Controls.Add(history); pages[0].Controls.Add(actions); pages[0].Controls.Add(summary); pages[0].Controls.Add(cards);

            // Controls: two explicit modes, each backed by the existing controller.
            Panel modeHead=new Panel { Dock=DockStyle.Top,Height=104,BackColor=Surface };
            controlMode=LabelAt("原有管理方式",20,17,500,36,20,Ink);
            controlTimer=LabelAt("尚未啟用低速",22,64,680,26,10,Muted);
            modeHead.Controls.AddRange(new Control[]{controlMode,controlTimer});
            sessionProgress=new SessionProgress { Dock=DockStyle.Bottom,Height=5,Value=0 }; modeHead.Controls.Add(sessionProgress);
            TableLayoutPanel modeTiles=Grid(2); modeTiles.Dock=DockStyle.Top; modeTiles.Height=232; modeTiles.Padding=new Padding(0,16,0,0);
            modeTiles.Controls.Add(ModeTile("安靜模式","約 1800 RPM","兩個風扇一起降速。每次最多五分鐘，適合低負載時使用。",QuietButton("啟用低速")),0,0);
            modeTiles.Controls.Add(ModeTile("原廠自動管理","依溫度調整","解除本工具的低速設定，交回 BIOS 管理散熱。",AutoButton()),1,0);
            Label controlNote=new Label { Dock=DockStyle.Fill,ForeColor=Muted,Padding=new Padding(3,20,5,0),Text="啟用條件\r\nACPI 熱區低於 60°C、GPU 低於 55°C，\r\nCPU 負載不超過 25%、GPU 負載不超過 30%。\r\n\r\n溫度或負載偏高時，面板會說明原因，保留原有散熱方式。",AutoSize=false };
            pages[1].Controls.Add(controlNote); pages[1].Controls.Add(modeTiles); pages[1].Controls.Add(modeHead);

            // Protection details and a session-only activity log.
            TableLayoutPanel protection=Grid(2); protection.Dock=DockStyle.Fill;
            Panel rules=new Panel { Dock=DockStyle.Fill,BackColor=Surface,Margin=new Padding(0,0,8,0),Padding=new Padding(18) };
            Label rulesTitle=new Label { Text="自動恢復條件",Dock=DockStyle.Top,Height=44,Font=new Font(Font.FontFamily,15),ForeColor=Ink };
            Label ruleText=new Label { Dock=DockStyle.Fill,ForeColor=Muted,Text="• ACPI 熱區達 60°C\r\n\r\n• GPU 溫度達 55°C\r\n\r\n• 運算負載升高\r\n\r\n• 溫度或風扇讀值失效\r\n\r\n• 本輪低速滿五分鐘\r\n\r\n• 正常關閉程式\r\n\r\n另有獨立備援程序，在主程式中斷時嘗試恢復散熱。" };
            rules.Controls.Add(ruleText); rules.Controls.Add(rulesTitle);
            Panel logs=new Panel { Dock=DockStyle.Fill,BackColor=Surface,Margin=new Padding(8,0,0,0),Padding=new Padding(16) };
            logs.Controls.Add(new Label { Text="本次操作紀錄",Dock=DockStyle.Top,Height=44,Font=new Font(Font.FontFamily,15),ForeColor=Ink });
            eventLog=new TextBox { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Surface,ForeColor=Muted,Font=new Font("Microsoft JhengHei UI",9) };
            logs.Controls.Add(eventLog); eventLog.BringToFront();
            protection.Controls.Add(rules,0,0); protection.Controls.Add(logs,1,0); pages[2].Controls.Add(protection);

            admin.Click+=delegate { if(preview)return; try { ProcessStartInfo start=new ProcessStartInfo(Application.ExecutablePath); start.UseShellExecute=true; start.Verb="runas"; Process.Start(start); allowClose=true; Close(); } catch(Win32Exception failure) { error=failure.NativeErrorCode==1223?"已取消提升權限，未改變風扇。":failure.Message; UpdateView(false); } };
            timer=new System.Windows.Forms.Timer { Interval=2000 };
            timer.Tick+=async delegate { if(!busy && session!=null) await Run(delegate{session.Poll();},false); };
            Shown+=async delegate
            {
                Rectangle screen=Screen.FromControl(this).WorkingArea;
                if(Width>screen.Width || Height>screen.Height) { Size=new Size(Math.Min(Width,screen.Width),Math.Min(Height,screen.Height)); Location=screen.Location; }
                if(preview) { PaintPreview(); return; }
                Log("控制面板已開啟；尚未變更風扇。");
                if(!Program.IsAdmin()) { UpdateView(false); return; }
                await Run(delegate{device=new HpQuietDevice();guard=new ProcessRecoveryGuard();session=new QuietSession(device,guard,()=>HpQuietDevice.Seconds);session.Poll();});
                timer.Start();
            };
            FormClosing+=async delegate(object sender,FormClosingEventArgs args)
            {
                if(allowClose || preview)return;
                if(busy){args.Cancel=true;closePending=true;return;}
                if(session==null || !session.NeedsRecovery)return;
                args.Cancel=true;timer.Stop();
                await Run(delegate{session.Restore("已恢復自動散熱，可以關閉。");});
                if(!session.NeedsRecovery){allowClose=true;Close();}else timer.Start();
            };
            FormClosed+=delegate{timer.Stop();timer.Dispose();if(guard!=null)guard.Dispose();if(device!=null)device.Dispose();};
            ShowPage(0); UpdateView(false);
        }

        Label LabelAt(string text,int x,int y,int width,int height,int size,Color color)
        { return new Label { Text=text,Location=new Point(x,y),Size=new Size(width,height),Font=new Font(Font.FontFamily,size),ForeColor=color }; }
        TableLayoutPanel Grid(int columns)
        {
            TableLayoutPanel table=new TableLayoutPanel{ColumnCount=columns,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
            for(int i=0;i<columns;i++)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));
            table.RowStyles.Add(new RowStyle(SizeType.Percent,100));return table;
        }
        Label Metric(TableLayoutPanel parent,int column,string name,string unit,Color color)
        {
            Panel card=new Panel{Dock=DockStyle.Fill,BackColor=Surface,Margin=new Padding(column==0?0:6,0,column==3?0:6,0),Padding=new Padding(14,12,8,9)};
            card.Controls.Add(new Label{Text=name,Dock=DockStyle.Top,Height=26,ForeColor=Muted});
            Label value=new Label{Text="—",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,25),ForeColor=color,TextAlign=ContentAlignment.MiddleLeft};
            card.Controls.Add(value);value.BringToFront();
            card.Controls.Add(new Label{Text=unit,Dock=DockStyle.Bottom,Height=21,Font=new Font(Font.FontFamily,8),ForeColor=Muted});
            parent.Controls.Add(card,column,0);return value;
        }
        Button FlatButton(string text,bool primary)
        {
            Button button=new DashboardButton{Text=text,FlatStyle=FlatStyle.Flat,BackColor=primary?Color.FromArgb(32,91,92):Color.FromArgb(28,44,62),ForeColor=Ink,Cursor=Cursors.Hand,Font=new Font(Font.FontFamily,10),Margin=new Padding(0,0,8,0),Dock=DockStyle.None};
            button.FlatAppearance.BorderSize=1;button.FlatAppearance.BorderColor=primary?Color.FromArgb(47,132,127):Color.FromArgb(47,64,84);return button;
        }
        Button QuietButton(string text)
        {
            Button button=FlatButton(text,true);button.Dock=DockStyle.Fill;quietButtons.Add(button);
            button.Click+=async delegate{if(session!=null)await Run(delegate{session.Enable();});};return button;
        }
        Button AutoButton()
        {
            Button button=FlatButton("恢復自動散熱",false);button.Dock=DockStyle.Fill;autoButtons.Add(button);
            button.Click+=async delegate{if(session!=null)await Run(delegate{session.Restore("已恢復原廠自動散熱。");});};return button;
        }
        Panel ModeTile(string heading,string value,string description,Button button)
        {
            Panel card=new Panel{Dock=DockStyle.Fill,BackColor=Surface,Margin=new Padding(0,0,12,0),Padding=new Padding(18)};
            card.Controls.Add(new Label{Text=heading,Dock=DockStyle.Top,Height=30,ForeColor=Muted});
            Label large=new Label{Text=value,Dock=DockStyle.Top,Height=44,Font=new Font(Font.FontFamily,20),ForeColor=Accent};
            Label text=new Label{Text=description,Dock=DockStyle.Fill,ForeColor=Muted,Padding=new Padding(0,8,0,0)};
            button.Dock=DockStyle.Bottom;button.Height=42;
            card.Controls.Add(text);card.Controls.Add(large);card.Controls.Add(button);large.BringToFront();text.BringToFront();return card;
        }
        void ShowPage(int index)
        {
            string[] titles={"即時總覽","風扇控制","保護與紀錄"};
            string[] descriptions={"查看即時狀態，快速切換已驗證的安靜模式。","選擇低速或原廠自動管理，查看本輪剩餘時間。","查看恢復條件，以及本次開啟期間的操作紀錄。"};
            for(int i=0;i<3;i++){pages[i].Visible=i==index;navigation[i].BackColor=i==index?Color.FromArgb(31,72,81):Color.FromArgb(18,28,41);navigation[i].ForeColor=i==index?Accent:Muted;}
            pages[index].BringToFront();title.Text=titles[index];subtitle.Text=descriptions[index];
        }
        void Log(string message)
        {
            events.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+message);if(events.Count>120)events.RemoveAt(0);
            eventLog.Text=String.Join(Environment.NewLine+Environment.NewLine,events.ToArray());eventLog.SelectionStart=eventLog.TextLength;eventLog.ScrollToCaret();
        }
        async Task Run(Action action,bool clearError=true)
        {
            if(busy)return;busy=true;if(clearError)error="";UpdateView(false);
            try{await Task.Run(action);}catch(Exception failure){error=failure.Message;}
            finally{busy=false;if(!IsDisposed)UpdateView(true);if(closePending && !IsDisposed){closePending=false;BeginInvoke(new Action(Close));}}
        }
        static string Value(double value){return Double.IsNaN(value)?"—":value.ToString("0");}
        void Fill(FanSample data,CoolingMode state,int seconds,bool needsRecovery,bool connected)
        {
            fan1.Text=data==null?"—":(data.Fan1*100).ToString();fan2.Text=data==null?"—":(data.Fan2*100).ToString();
            zone.Text=data==null?"—":Value(data.ZoneC);gpu.Text=data==null?"—":Value(data.GpuC);
            load.Text=data==null?"CPU 負載 —　 GPU 負載 —":"CPU 負載 "+Value(data.CpuLoad)+"%　 GPU 負載 "+Value(data.GpuLoad)+"%";
            string modeName=state==CoolingMode.Quiet?"安靜模式":state==CoolingMode.RecoveryFailed?"恢復待確認":"原有管理方式";
            currentMode.Text="目前："+modeName;controlMode.Text=modeName;
            countdown.Text=state==CoolingMode.Quiet?"本輪剩餘 "+TimeSpan.FromSeconds(seconds).ToString(@"m\:ss"):"每次低速最多 5 分鐘";
            controlTimer.Text=state==CoolingMode.Quiet?"目標約 1800 RPM · "+countdown.Text:"按下安靜模式後才會套用低速。";
            sessionProgress.Value=Math.Max(0,Math.Min(300,seconds));
            connection.Text=connected?"●  已取得管理員權限":"○  尚未取得控制權限";connection.ForeColor=connected?Accent:Muted;admin.Visible=!connected;admin.Enabled=!busy;
            foreach(Button button in quietButtons)button.Enabled=!busy && connected && !needsRecovery && data!=null && data.Valid;
            foreach(Button button in autoButtons)button.Enabled=!busy && connected && needsRecovery;
        }
        void UpdateView(bool record)
        {
            FanSample data=session==null?null:session.Latest;
            Fill(data,session==null?CoolingMode.Automatic:session.Mode,session==null?0:session.RemainingSeconds,session!=null && session.NeedsRecovery,Program.IsAdmin());
            string message=error.Length>0?error:session==null?"取得管理員權限後開始監看；目前未變更風扇。":session.Status;
            status.Text=(busy?"正在處理，請稍候…":message)+"\r\n轉速為 BIOS 等級估計；ACPI 熱區並非 CPU 核心溫度。";
            if(record && data!=null){history.Add(DateTime.UtcNow,data.Fan1*100,data.Fan2*100);refreshed.Text="更新 "+DateTime.Now.ToString("HH:mm:ss");}
            if(record && message!=loggedStatus){loggedStatus=message;Log(message);}
        }
        void PaintPreview()
        {
            bool locked=previewScene=="locked",automatic=previewScene=="auto";
            FanSample data=locked?null:new FanSample(54,47,12,8,automatic?27:18,automatic?29:18);
            Fill(data,locked||automatic?CoolingMode.Automatic:CoolingMode.Quiet,locked||automatic?0:272,!locked&&!automatic,!locked);
            if(!locked)for(int i=0;i<=60;i++)history.Add(DateTime.UtcNow.AddSeconds(-120+2*i),(int)(i<38?2850+Math.Sin(i*.35)*130:1800+Math.Sin(i)*45),(int)(i<38?3000+Math.Cos(i*.32)*110:1800+Math.Cos(i)*45));
            refreshed.Text=locked?"等待連線":"更新 "+DateTime.Now.ToString("HH:mm:ss");
            status.Text="介面預覽 · 範例讀值，未控制風扇。\r\n轉速為 BIOS 等級估計；ACPI 熱區並非 CPU 核心溫度。";
            Log("控制面板已開啟。");if(!locked){Log("管理員連線完成，開始監看。");Log("啟用安靜模式，目標約 1800 RPM。");}
            if(previewScene=="controls")ShowPage(1);if(previewScene=="protection")ShowPage(2);
        }
    }
}
