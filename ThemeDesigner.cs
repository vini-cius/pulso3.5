/* SPDX-License-Identifier: GPL-3.0-or-later
 * Copyright (C) 2026 Pulso 3.5 contributors
 * See LICENSE for the full license text.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Controlador35 {
    public sealed class ThemeDesignerForm : Form {
        readonly Color page=Color.FromArgb(11,18,30),panel=Color.FromArgb(18,28,43),panelRaised=Color.FromArgb(25,38,56),
            ink=Color.FromArgb(235,242,249),muted=Color.FromArgb(144,164,184),cyan=Color.FromArgb(91,229,213),amber=Color.FromArgb(255,180,84);
        ThemeConfig draft;
        readonly Metrics liveMetrics;
        readonly Weather liveWeather;
        readonly Action<ThemeConfig> saved;
        readonly ThemeCanvasView canvasView=new ThemeCanvasView();
        readonly ListBox layers=new ListBox();
        readonly TextBox themeName=new TextBox(),widgetName=new TextBox(),widgetText=new TextBox();
        readonly ComboBox savedThemes=new ComboBox(),globalFont=new ComboBox(),alignBox=new ComboBox(),fitBox=new ComboBox(),shapeBox=new ComboBox(),backgroundFit=new ComboBox(),palette=new ComboBox();
        readonly NumericUpDown posX=new NumericUpDown(),posY=new NumericUpDown(),sizeW=new NumericUpDown(),sizeH=new NumericUpDown(),fontSize=new NumericUpDown();
        readonly CheckBox seconds=new CheckBox(),bold=new CheckBox(),visible=new CheckBox(),useFill=new CheckBox(),grid=new CheckBox();
        readonly Button foregroundColor=new Button(),fillColor=new Button(),backgroundColor=new Button();
        Button undoButton,redoButton,portraitButton,landscapeButton,widgetImage,clearBackground;
        readonly Label coordinates=new Label(),widgetType=new Label(),backgroundAsset=new Label();
        readonly System.Windows.Forms.Panel inspectorPanel=new System.Windows.Forms.Panel();
        readonly Timer previewTimer=new Timer { Interval=1000 };
        readonly Stack<ThemeConfig> undo=new Stack<ThemeConfig>(),redo=new Stack<ThemeConfig>();
        ThemeWidget selected;
        int selectedSectionTop;
        Label selectedSectionHeader;
        bool landscape,loading;
        public ThemeDesignerForm(ThemeConfig current,Metrics metrics,Weather weather,Action<ThemeConfig> onSaved) {
            draft=current==null?new ThemeConfig():current.Clone();ThemeDefaults.Ensure(draft,false);liveMetrics=metrics;liveWeather=weather;saved=onSaved;
            landscape=String.Equals(draft.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase);
            Text="Pulso 3.5 — Estúdio de temas";StartPosition=FormStartPosition.CenterParent;WindowState=FormWindowState.Maximized;
            MinimumSize=new Size(1280,740);BackColor=page;ForeColor=ink;Font=new Font("Segoe UI",9.5f);KeyPreview=true;
            BuildWorkbench();BuildInspector();FillSavedThemes();BindCanvas();RebuildLayers(null);RefreshFrame();UpdateModeButtons();UpdateHistoryButtons();
            previewTimer.Tick+=delegate{RefreshFrame();};previewTimer.Start();
            KeyDown+=HandleKeyboard;FormClosed+=delegate{previewTimer.Stop();previewTimer.Dispose();canvasView.DisposeFrame();};
        }
        ThemeCanvas ActiveCanvas { get { ThemeDefaults.Ensure(draft,false);return landscape?draft.Landscape:draft.Portrait; } }
        void BuildWorkbench() {
            var header=new System.Windows.Forms.Panel { Dock=DockStyle.Fill,BackColor=panel,Padding=new Padding(16,10,16,8) };
            var brand=new Label { Text="ESTÚDIO 35",Font=new Font("Segoe UI",15,FontStyle.Bold),ForeColor=cyan,AutoSize=true,Location=new Point(17,7) };header.Controls.Add(brand);
            var sub=new Label { Text="Componha o seu painel",Font=new Font("Segoe UI",8.5f),ForeColor=muted,AutoSize=true,Location=new Point(19,37) };header.Controls.Add(sub);
            themeName.SetBounds(158,18,150,31);themeName.Text=draft.Name;StyleText(themeName);header.Controls.Add(themeName);
            savedThemes.DropDownStyle=ComboBoxStyle.DropDownList;savedThemes.SetBounds(315,18,115,31);StyleCombo(savedThemes);header.Controls.Add(savedThemes);
            globalFont.DropDownStyle=ComboBoxStyle.DropDownList;globalFont.SetBounds(435,18,110,31);StyleCombo(globalFont);
            globalFont.Items.AddRange(new object[]{"Segoe UI","Arial","Bahnschrift","Calibri","Consolas","Georgia","Impact","Trebuchet MS"});
            globalFont.SelectedItem=draft.Font;if(globalFont.SelectedIndex<0)globalFont.SelectedIndex=0;header.Controls.Add(globalFont);
            portraitButton=MakeButton("Retrato",new Size(72,32));portraitButton.SetBounds(552,17,72,32);portraitButton.Click+=delegate{SetOrientation(false);};header.Controls.Add(portraitButton);portraitButton.Tag="portrait";
            landscapeButton=MakeButton("Paisagem",new Size(80,32));landscapeButton.SetBounds(628,17,80,32);landscapeButton.Click+=delegate{SetOrientation(true);};header.Controls.Add(landscapeButton);landscapeButton.Tag="landscape";
            seconds.Text="Segundos";seconds.AutoSize=true;seconds.Checked=draft.ShowSeconds;seconds.ForeColor=ink;seconds.Location=new Point(716,24);header.Controls.Add(seconds);
            var bg=MakeButton("Imagem de fundo",new Size(108,32));bg.SetBounds(806,17,108,32);bg.Click+=ChooseBackground;header.Controls.Add(bg);
            undoButton=MakeButton("↶",new Size(32,32));undoButton.SetBounds(920,17,32,32);undoButton.Click+=delegate{UndoChange();};header.Controls.Add(undoButton);
            redoButton=MakeButton("↷",new Size(32,32));redoButton.SetBounds(955,17,32,32);redoButton.Click+=delegate{RedoChange();};header.Controls.Add(redoButton);
            var import=MakeButton("Importar",new Size(64,32));import.SetBounds(992,17,64,32);import.Click+=ImportTheme;header.Controls.Add(import);
            var export=MakeButton("Exportar",new Size(64,32));export.SetBounds(1060,17,64,32);export.Click+=ExportTheme;header.Controls.Add(export);
            var save=MakeButton("Salvar",new Size(64,32),cyan,true);save.SetBounds(1128,17,64,32);save.Click+=SaveTheme;header.Controls.Add(save);
            var close=MakeButton("Fechar",new Size(60,32));close.SetBounds(1198,17,60,32);close.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};header.Controls.Add(close);

            var body=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new Padding(12,10,12,12),BackColor=page,Margin=Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,218));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,292));
            var left=new System.Windows.Forms.Panel { Dock=DockStyle.Fill,BackColor=panel,Padding=new Padding(12) };body.Controls.Add(left,0,0);
            var layersTitle=new Label {Text="CAMADAS",Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=muted,AutoSize=true,Location=new Point(12,14)};left.Controls.Add(layersTitle);
            var add=MakeButton("＋ Adicionar elemento",new Size(190,34),cyan,true);add.SetBounds(12,42,190,34);left.Controls.Add(add);add.Click+=OpenAddMenu;
            var menu=new ContextMenuStrip();
            AddMenu(menu,"Relógio","Clock");AddMenu(menu,"Data","Date");AddMenu(menu,"CPU","CPU");AddMenu(menu,"Memória RAM","RAM");AddMenu(menu,"Uso da GPU","GPU");
            AddMenu(menu,"Temperatura da CPU","CPU_TEMP");AddMenu(menu,"Temperatura da GPU","GPU_TEMP");AddMenu(menu,"Clima","Weather");
            menu.Items.Add(new ToolStripSeparator());AddMenu(menu,"Medidor / barra","Gauge");AddMenu(menu,"Texto livre","Text");AddMenu(menu,"Imagem","Image");AddMenu(menu,"Forma","Shape");
            add.ContextMenuStrip=menu;layers.SetBounds(12,86,190,320);layers.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;layers.DrawMode=DrawMode.OwnerDrawFixed;layers.ItemHeight=42;layers.BorderStyle=BorderStyle.None;
            layers.BackColor=panel;layers.ForeColor=ink;left.Controls.Add(layers);layers.DrawItem+=DrawLayer;layers.SelectedIndexChanged+=LayerChanged;
            var row1=new FlowLayoutPanel { FlowDirection=FlowDirection.LeftToRight,WrapContents=false,AutoSize=true,Location=new Point(12,450),Width=190,Anchor=AnchorStyles.Bottom|AnchorStyles.Left };
            var duplicate=MakeButton("Duplicar",new Size(88,31));duplicate.Click+=delegate{DuplicateSelected();};row1.Controls.Add(duplicate);
            var delete=MakeButton("Excluir",new Size(88,31),Color.FromArgb(255,130,122));delete.Click+=delegate{DeleteSelected();};row1.Controls.Add(delete);left.Controls.Add(row1);
            var row2=new FlowLayoutPanel { FlowDirection=FlowDirection.LeftToRight,WrapContents=false,AutoSize=true,Location=new Point(12,490),Width=190,Anchor=AnchorStyles.Bottom|AnchorStyles.Left };
            var up=MakeButton("Subir",new Size(88,31));up.Click+=delegate{MoveLayer(-1);};row2.Controls.Add(up);var down=MakeButton("Descer",new Size(88,31));down.Click+=delegate{MoveLayer(1);};row2.Controls.Add(down);left.Controls.Add(row2);
            Action arrangeLayers=delegate {
                int h=left.ClientSize.Height;layers.SetBounds(12,86,Math.Max(120,left.ClientSize.Width-24),Math.Max(130,h-86-104));
                row1.Location=new Point(12,Math.Max(420,h-78));row2.Location=new Point(12,Math.Max(455,h-40));
            };
            left.Resize+=delegate{arrangeLayers();};Shown+=delegate{arrangeLayers();};
            var center=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=Color.FromArgb(13,20,32),Margin=new Padding(10,0,10,0),Padding=Padding.Empty };
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));center.RowStyles.Add(new RowStyle(SizeType.Absolute,44));center.RowStyles.Add(new RowStyle(SizeType.Percent,100));center.RowStyles.Add(new RowStyle(SizeType.Absolute,28));body.Controls.Add(center,1,0);
            var canvasHeader=new System.Windows.Forms.Panel {Dock=DockStyle.Fill,BackColor=panelRaised,Margin=Padding.Empty};center.Controls.Add(canvasHeader,0,0);
            var canvasTitle=new Label {Text="PRÉVIA DO PAINEL",AutoSize=true,Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=ink,Location=new Point(14,14)};canvasHeader.Controls.Add(canvasTitle);
            grid.Text="Grade e encaixe 8 px";grid.Checked=true;grid.AutoSize=true;grid.ForeColor=muted;grid.Location=new Point(170,13);canvasHeader.Controls.Add(grid);
            coordinates.Text="320 × 480 px";coordinates.AutoSize=true;coordinates.ForeColor=cyan;coordinates.Font=new Font("Consolas",9,FontStyle.Bold);coordinates.Location=new Point(370,14);canvasHeader.Controls.Add(coordinates);
            canvasView.Dock=DockStyle.Fill;canvasView.BackColor=Color.FromArgb(13,20,32);canvasView.Margin=Padding.Empty;center.Controls.Add(canvasView,0,1);
            var centerFoot=new Label {Text="Arraste para posicionar  •  puxe o canto inferior direito para redimensionar",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=muted,BackColor=panel,Margin=Padding.Empty};center.Controls.Add(centerFoot,0,2);
            var right=new System.Windows.Forms.Panel {Dock=DockStyle.Fill,BackColor=panel,Padding=new Padding(0)};body.Controls.Add(right,2,0);
            var rightLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=panel,Padding=Padding.Empty,Margin=Padding.Empty};
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.Controls.Add(rightLayout);
            var inspectorHead=new Label {Text="PROPRIEDADES",Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=muted,Dock=DockStyle.Fill,Padding=new Padding(14,13,0,0),Margin=Padding.Empty};rightLayout.Controls.Add(inspectorHead,0,0);
            inspectorPanel.Dock=DockStyle.Fill;inspectorPanel.AutoScroll=true;inspectorPanel.Padding=new Padding(14,4,14,12);inspectorPanel.Margin=Padding.Empty;rightLayout.Controls.Add(inspectorPanel,0,1);
            var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=page,Padding=Padding.Empty,Margin=Padding.Empty};
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,74));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            shell.Controls.Add(header,0,0);shell.Controls.Add(body,0,1);Controls.Add(shell);
            layers.ContextMenuStrip=menu;
            themeName.TextChanged+=delegate{if(loading)return;RecordUndo();draft.Name=String.IsNullOrWhiteSpace(themeName.Text)?"Meu tema":themeName.Text.Trim();};
            seconds.CheckedChanged+=delegate{if(loading)return;RecordUndo();draft.ShowSeconds=seconds.Checked;RefreshFrame();};
            globalFont.SelectedIndexChanged+=delegate{if(loading||globalFont.SelectedItem==null)return;RecordUndo();draft.Font=globalFont.SelectedItem.ToString();foreach(var item in draft.Portrait.Widgets.Concat(draft.Landscape.Widgets))item.Font=draft.Font;RefreshFrame();RefreshInspector();};
            grid.CheckedChanged+=delegate{canvasView.ShowGrid=grid.Checked;canvasView.Snap=grid.Checked;canvasView.Invalidate();};
        }
        void BuildInspector() {
            int y=4;AddInspectorLabel("TELA ATUAL",y);y+=24;
            backgroundAsset.SetBounds(0,y,242,25);backgroundAsset.ForeColor=muted;backgroundAsset.AutoEllipsis=true;inspectorPanel.Controls.Add(backgroundAsset);y+=27;
            backgroundColor.Text="Cor de fundo";backgroundColor.Size=new Size(110,29);backgroundColor.SetBounds(0,y,110,29);StyleColorButton(backgroundColor);backgroundColor.Click+=ChooseBackgroundColor;inspectorPanel.Controls.Add(backgroundColor);
            palette.DropDownStyle=ComboBoxStyle.DropDownList;palette.Items.AddRange(new object[]{"Personalizada","Oceano","Grafite","Amanhecer"});palette.SetBounds(116,y,126,29);StyleCombo(palette);inspectorPanel.Controls.Add(palette);palette.SelectedItem=new[]{"Oceano","Grafite","Amanhecer"}.Contains(draft.Name)?draft.Name:"Personalizada";palette.SelectedIndexChanged+=ApplyPalette;
            y+=37;AddInspectorLabel("AJUSTE DA IMAGEM",y);y+=19;
            backgroundFit.DropDownStyle=ComboBoxStyle.DropDownList;backgroundFit.Items.AddRange(new object[]{"Cover","Contain","Stretch"});backgroundFit.SetBounds(0,y,112,29);StyleCombo(backgroundFit);inspectorPanel.Controls.Add(backgroundFit);
            clearBackground=MakeButton("Remover imagem",new Size(124,29));clearBackground.SetBounds(118,y,124,29);clearBackground.Click+=delegate{RecordUndo();ActiveCanvas.BackgroundImage="";RefreshInspector();RefreshFrame();};inspectorPanel.Controls.Add(clearBackground);
            backgroundFit.SelectedIndexChanged+=delegate{if(loading)return;RecordUndo();ActiveCanvas.BackgroundFit=backgroundFit.SelectedItem.ToString();RefreshFrame();};
            y+=42;selectedSectionTop=y;selectedSectionHeader=AddInspectorLabel("ELEMENTO SELECIONADO",y);y+=25;
            widgetType.SetBounds(0,y,242,22);widgetType.ForeColor=cyan;widgetType.Font=new Font("Consolas",9,FontStyle.Bold);inspectorPanel.Controls.Add(widgetType);y+=28;
            AddInspectorLabel("Nome da camada",y);y+=21;widgetName.SetBounds(0,y,242,30);StyleText(widgetName);inspectorPanel.Controls.Add(widgetName);y+=40;
            AddInspectorLabel("Texto ou conteúdo",y);y+=20;widgetText.SetBounds(0,y,242,57);widgetText.Multiline=true;widgetText.ScrollBars=ScrollBars.Vertical;StyleText(widgetText);inspectorPanel.Controls.Add(widgetText);
            widgetImage=MakeButton("Escolher imagem…",new Size(242,27));widgetImage.SetBounds(0,y+60,242,27);widgetImage.Click+=ChooseWidgetImage;inspectorPanel.Controls.Add(widgetImage);y+=96;
            AddInspectorLabel("Posição / largura / altura (px)",y);y+=19;
            var gridBounds=new TableLayoutPanel {Location=new Point(0,y),Size=new Size(244,99),ColumnCount=4,RowCount=2,BackColor=panel};
            gridBounds.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));gridBounds.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));gridBounds.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));gridBounds.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            gridBounds.RowStyles.Add(new RowStyle(SizeType.Absolute,27));gridBounds.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            AddGridCell(gridBounds,"X",0,0);AddGridCell(gridBounds,"Y",2,0);AddGridCell(gridBounds,"L",0,1);AddGridCell(gridBounds,"A",2,1);
            ConfigureNumber(posX,gridBounds,1,0);ConfigureNumber(posY,gridBounds,3,0);ConfigureNumber(sizeW,gridBounds,1,1);ConfigureNumber(sizeH,gridBounds,3,1);inspectorPanel.Controls.Add(gridBounds);y+=109;
            AddInspectorLabel("Fonte e alinhamento",y);y+=22;
            fontSize.Minimum=7;fontSize.Maximum=160;fontSize.SetBounds(0,y,76,29);StyleNumber(fontSize);inspectorPanel.Controls.Add(fontSize);
            alignBox.DropDownStyle=ComboBoxStyle.DropDownList;alignBox.Items.AddRange(new object[]{"Left","Center","Right"});alignBox.SetBounds(82,y,160,29);StyleCombo(alignBox);inspectorPanel.Controls.Add(alignBox);y+=40;
            bold.Text="Negrito";bold.AutoSize=true;bold.ForeColor=ink;bold.SetBounds(0,y,88,24);inspectorPanel.Controls.Add(bold);
            visible.Text="Visível";visible.AutoSize=true;visible.ForeColor=ink;visible.SetBounds(106,y,88,24);inspectorPanel.Controls.Add(visible);y+=33;
            var colors=new FlowLayoutPanel {Location=new Point(0,y),Size=new Size(248,34),WrapContents=false};
            foregroundColor.Text="Texto / linha";foregroundColor.Size=new Size(119,29);StyleColorButton(foregroundColor);foregroundColor.Click+=ChooseWidgetColor;colors.Controls.Add(foregroundColor);
            fillColor.Text="Preenchimento";fillColor.Size=new Size(119,29);StyleColorButton(fillColor);fillColor.Click+=ChooseFillColor;colors.Controls.Add(fillColor);inspectorPanel.Controls.Add(colors);y+=41;
            useFill.Text="Preencher forma ou cartão";useFill.AutoSize=true;useFill.ForeColor=ink;useFill.SetBounds(0,y,230,24);inspectorPanel.Controls.Add(useFill);y+=32;
            shapeBox.DropDownStyle=ComboBoxStyle.DropDownList;shapeBox.Items.AddRange(new object[]{"Rectangle","Ellipse","Line"});shapeBox.SetBounds(0,y,112,29);StyleCombo(shapeBox);inspectorPanel.Controls.Add(shapeBox);
            fitBox.DropDownStyle=ComboBoxStyle.DropDownList;fitBox.Items.AddRange(new object[]{"Cover","Contain","Stretch"});fitBox.SetBounds(120,y,122,29);StyleCombo(fitBox);inspectorPanel.Controls.Add(fitBox);y+=40;
            var tokenHint=new Label {Text="Texto dinâmico: %CPU%  %RAM%  %GPU%\n%CPU_TEMP%  %GPU_TEMP%  %TIME%\n%DATE%  %CITY%  %WEATHER%",AutoSize=false,Size=new Size(244,55),ForeColor=muted,Font=new Font("Consolas",8.5f)};tokenHint.SetBounds(0,y,244,55);inspectorPanel.Controls.Add(tokenHint);
            widgetName.TextChanged+=WidgetTextChanged;widgetText.TextChanged+=WidgetTextChanged;
            posX.ValueChanged+=BoundsChanged;posY.ValueChanged+=BoundsChanged;sizeW.ValueChanged+=BoundsChanged;sizeH.ValueChanged+=BoundsChanged;fontSize.ValueChanged+=FontSizeChanged;
            alignBox.SelectedIndexChanged+=OnStyleChanged;bold.CheckedChanged+=OnStyleChanged;visible.CheckedChanged+=OnStyleChanged;useFill.CheckedChanged+=OnStyleChanged;
            fitBox.SelectedIndexChanged+=OnStyleChanged;shapeBox.SelectedIndexChanged+=OnStyleChanged;
        }
        void BindCanvas() {
            canvasView.GetFrame=delegate{return Panel.Render(DateTime.Now,PreviewMetrics(),liveWeather??new Weather(),draft);};
            canvasView.GetTheme=delegate{return draft;};canvasView.GetSelected=delegate{return selected;};canvasView.Snap=grid.Checked;canvasView.ShowGrid=grid.Checked;
            canvasView.DragStarted+=delegate{RecordUndo();};canvasView.PositionChanged+=delegate(ThemeWidget item){coordinates.Text=item.X+", "+item.Y+" px  •  "+item.Width+" × "+item.Height;RefreshInspector();layers.Invalidate();RefreshFrame();};
            canvasView.SelectionChanged+=delegate(ThemeWidget item){SelectWidget(item);};
        }
        Metrics PreviewMetrics() {
            if(liveMetrics!=null&&liveMetrics.At!=default(DateTime)&&(DateTime.Now-liveMetrics.At).TotalSeconds<15)return liveMetrics;
            return new Metrics {Cpu=31,Ram=54,Gpu=18,CpuTemperature=48,GpuTemperature=43,UsedGb=8.2,TotalGb=16,At=DateTime.Now};
        }
        void AddGridCell(TableLayoutPanel table,string text,int column,int row) {
            var label=new Label {Text=text,ForeColor=muted,TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill};table.Controls.Add(label,column,row);
        }
        void ConfigureNumber(NumericUpDown input,TableLayoutPanel host,int column,int row) {input.Minimum=0;input.Maximum=480;input.Increment=1;StyleNumber(input);input.Dock=DockStyle.Fill;host.Controls.Add(input,column,row);}
        Label AddInspectorLabel(string text,int y) {var label=new Label{Text=text,AutoSize=true,ForeColor=muted,Font=new Font("Segoe UI",8,FontStyle.Bold),Location=new Point(0,y)};inspectorPanel.Controls.Add(label);return label;}
        void AddMenu(ContextMenuStrip menu,string label,string type) {var item=new ToolStripMenuItem(label);item.Tag=type;item.Click+=delegate{AddWidget((string)item.Tag);};menu.Items.Add(item);}
        void OpenAddMenu(object sender,EventArgs e) { var button=(Button)sender;var menu=button.ContextMenuStrip;if(menu!=null)menu.Show(button,new Point(0,button.Height)); }
        void FillSavedThemes() {
            savedThemes.Items.Clear();savedThemes.Items.Add("Temas salvos");foreach(var item in ThemeStore.LoadSaved())savedThemes.Items.Add(item);
            savedThemes.DisplayMember="Name";savedThemes.SelectedIndex=0;savedThemes.SelectedIndexChanged+=delegate{var choice=savedThemes.SelectedItem as ThemeConfig;if(choice==null)return;
                if(MessageBox.Show(this,"Abrir este tema e substituir o rascunho atual?","Abrir tema salvo",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                RecordUndo();draft=choice.Clone();ThemeDefaults.Ensure(draft,false);landscape=String.Equals(draft.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase);themeName.Text=draft.Name;seconds.Checked=draft.ShowSeconds;globalFont.SelectedItem=draft.Font;SetOrientation(landscape);RebuildLayers(null);RefreshInspector();RefreshFrame();};
        }
        void RebuildLayers(string selectedId) {
            layers.BeginUpdate();layers.Items.Clear();foreach(var item in ActiveCanvas.Widgets)if(item!=null)layers.Items.Add(item);layers.EndUpdate();
            int index=-1;for(int i=0;i<layers.Items.Count;i++)if(selectedId!=null&&((ThemeWidget)layers.Items[i]).Id==selectedId){index=i;break;}
            if(index<0&&layers.Items.Count>0)index=0;
            layers.SelectedIndex=index;selected=index<0?null:(ThemeWidget)layers.Items[index];layers.Invalidate();RefreshInspector();
        }
        void DrawLayer(object sender,DrawItemEventArgs e) {
            e.DrawBackground();if(e.Index<0)return;var item=(ThemeWidget)layers.Items[e.Index];bool chosen=(e.State&DrawItemState.Selected)!=0;
            using(var b=new SolidBrush(chosen?panelRaised:panel))e.Graphics.FillRectangle(b,e.Bounds);
            if(chosen)using(var p=new Pen(amber,2))e.Graphics.DrawLine(p,e.Bounds.Left+1,e.Bounds.Top+5,e.Bounds.Left+1,e.Bounds.Bottom-5);
            using(var title=new SolidBrush(item.Visible?ink:muted))using(var font=new Font("Segoe UI",9,chosen?FontStyle.Bold:FontStyle.Regular))e.Graphics.DrawString(String.IsNullOrWhiteSpace(item.Name)?item.Type:item.Name,font,title,e.Bounds.Left+12,e.Bounds.Top+6);
            using(var note=new SolidBrush(muted))using(var font=new Font("Consolas",7.5f))e.Graphics.DrawString(item.Type+"  •  "+item.X+","+item.Y+"  "+item.Width+"×"+item.Height,font,note,e.Bounds.Left+12,e.Bounds.Top+23);
            e.DrawFocusRectangle();
        }
        void LayerChanged(object sender,EventArgs e) { selected=layers.SelectedItem as ThemeWidget;RefreshInspector();canvasView.Invalidate(); }
        void SelectWidget(ThemeWidget item) {
            if(item==null){selected=null;RefreshInspector();RefreshFrame();canvasView.Invalidate();return;}
            selected=item;int index=-1;for(int i=0;i<layers.Items.Count;i++)if(((ThemeWidget)layers.Items[i]).Id==item.Id){index=i;break;}
            if(layers.SelectedIndex!=index)layers.SelectedIndex=index;else{RefreshInspector();canvasView.Invalidate();}
        }
        void SetOrientation(bool horizontal) {
            if(landscape!=horizontal)RecordUndo();
            landscape=horizontal;draft.Orientation=landscape?"Landscape":"Portrait";selected=null;UpdateModeButtons();
            coordinates.Text=(landscape?"480 × 320":"320 × 480")+" px";canvasView.Invalidate();RebuildLayers(null);RefreshInspector();RefreshFrame();
        }
        void UpdateModeButtons() {if(portraitButton!=null)StyleModeButton(portraitButton,!landscape);if(landscapeButton!=null)StyleModeButton(landscapeButton,landscape);}
        void StyleModeButton(Button b,bool active) {b.BackColor=active?Color.FromArgb(35,78,87):panelRaised;b.ForeColor=active?cyan:ink;}
        void AddWidget(string type) {
            RecordUndo();int width=landscape?480:320,height=landscape?320:480;
            int w=type=="Weather"?Math.Min(230,width-24):type=="Clock"?Math.Min(220,width-24):type=="Shape"?100:145;
            int h=type=="Weather"?74:type=="Clock"?56:type=="Shape"?70:40;
            var item=new ThemeWidget {Type=type,Name=DisplayName(type),X=Math.Max(0,(width-w)/2),Y=Math.Max(0,(height-h)/2),Width=w,Height=h,Font=draft.Font,FontSize=type=="Clock"?36:20,Text=type=="Text"?"Seu texto":type=="Gauge"?"CPU":"",Bold=type=="Clock"};
            if(type=="CPU")item.AccentKey="CpuAccent";if(type=="RAM")item.AccentKey="RamAccent";if(type=="GPU")item.AccentKey="GpuAccent";
            if(type=="CPU_TEMP")item.AccentKey="CpuAccent";if(type=="GPU_TEMP")item.AccentKey="GpuAccent";if(type=="Weather")item.AccentKey="WeatherAccent";
            if(type=="Image") { using(var dialog=new OpenFileDialog {Title="Escolha a imagem do elemento",Filter="Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif"})if(dialog.ShowDialog(this)!=DialogResult.OK)return;else item.Text=ThemeStore.StoreImage(dialog.FileName); }
            ActiveCanvas.Widgets.Add(item);RebuildLayers(item.Id);RefreshFrame();
        }
        string DisplayName(string type) {
            switch(type){case "Clock":return "Relógio";case "Date":return "Data";case "CPU":return "CPU";case "RAM":return "Memória RAM";case "GPU":return "Uso da GPU";case "CPU_TEMP":return "Temperatura da CPU";case "GPU_TEMP":return "Temperatura da GPU";case "Weather":return "Clima";case "Gauge":return "Medidor";case "Text":return "Texto";case "Image":return "Imagem";case "Shape":return "Forma";default:return type;}
        }
        void DuplicateSelected() {if(selected==null)return;RecordUndo();var copy=selected.Clone();copy.Id=Guid.NewGuid().ToString("N");copy.Name=(copy.Name??"Elemento")+" cópia";copy.X=Math.Min(Math.Max(0,ActiveCanvas.Widgets.Select(x=>x.X).DefaultIfEmpty(0).Max()+8),Panel.Width(draft)-copy.Width);copy.Y=Math.Min(copy.Y+8,Panel.Height(draft)-copy.Height);ActiveCanvas.Widgets.Add(copy);RebuildLayers(copy.Id);RefreshFrame();}
        void DeleteSelected() {if(selected==null)return;RecordUndo();ActiveCanvas.Widgets.Remove(selected);RebuildLayers(null);RefreshFrame();}
        void MoveLayer(int offset) {if(selected==null)return;int current=ActiveCanvas.Widgets.IndexOf(selected),target=Math.Max(0,Math.Min(ActiveCanvas.Widgets.Count-1,current+offset));if(target==current)return;RecordUndo();ActiveCanvas.Widgets.RemoveAt(current);ActiveCanvas.Widgets.Insert(target,selected);RebuildLayers(selected.Id);RefreshFrame();}
        void WidgetTextChanged(object sender,EventArgs e) {if(loading||selected==null)return;RecordUndo();selected.Name=String.IsNullOrWhiteSpace(widgetName.Text)?DisplayName(selected.Type):widgetName.Text;selected.Text=widgetText.Text;layers.Invalidate();RefreshFrame();}
        void BoundsChanged(object sender,EventArgs e) {
            if(loading||selected==null)return;RecordUndo();int maxW=Panel.Width(draft),maxH=Panel.Height(draft);
            int w=Math.Max(12,Math.Min(maxW,(int)sizeW.Value)),h=Math.Max(12,Math.Min(maxH,(int)sizeH.Value));selected.X=Math.Max(0,Math.Min(maxW-w,(int)posX.Value));selected.Y=Math.Max(0,Math.Min(maxH-h,(int)posY.Value));selected.Width=w;selected.Height=h;
            canvasView.Invalidate();layers.Invalidate();coordinates.Text=selected.X+", "+selected.Y+" px  •  "+w+" × "+h;
        }
        void FontSizeChanged(object sender,EventArgs e) {if(loading||selected==null)return;RecordUndo();selected.FontSize=(int)fontSize.Value;if(selected.Type=="Clock")draft.ClockSize=selected.FontSize;RefreshFrame();}
        void OnStyleChanged(object sender,EventArgs e) {
            if(loading||selected==null)return;RecordUndo();selected.Align=alignBox.SelectedItem==null?"Left":alignBox.SelectedItem.ToString();selected.Bold=bold.Checked;selected.Visible=visible.Checked;selected.UseFill=useFill.Checked;
            selected.Fit=fitBox.SelectedItem==null?"Cover":fitBox.SelectedItem.ToString();selected.Shape=shapeBox.SelectedItem==null?"Rectangle":shapeBox.SelectedItem.ToString();
            RefreshFrame();layers.Invalidate();
        }
        void RefreshInspector() {
            loading=true;bool active=selected!=null;widgetType.Text=active?DisplayName(selected.Type).ToUpperInvariant():"Selecione um elemento no painel";
            foreach(Control c in inspectorPanel.Controls) {
                bool widgetProperty=c.Top>=selectedSectionTop;
                bool keepPrompt=c==selectedSectionHeader||c==widgetType;
                c.Visible=!widgetProperty||active||keepPrompt;
                c.Enabled=!widgetProperty||active||keepPrompt;
            }
            var canvas=ActiveCanvas;backgroundAsset.Text=String.IsNullOrWhiteSpace(canvas.BackgroundImage)?"Sem imagem de fundo":Path.GetFileName(canvas.BackgroundImage);
            backgroundFit.SelectedItem=canvas.BackgroundFit??"Cover";backgroundColor.BackColor=Color.FromArgb(unchecked((int)(0xFF000000u|(uint)canvas.Background)));backgroundColor.ForeColor=Contrast(backgroundColor.BackColor);
            if(active) {
                widgetName.Text=selected.Name??"";widgetText.Text=selected.Text??"";
                posX.Maximum=Panel.Width(draft)-1;posY.Maximum=Panel.Height(draft)-1;sizeW.Maximum=Panel.Width(draft);sizeH.Maximum=Panel.Height(draft);
                posX.Value=Math.Max(0,Math.Min(posX.Maximum,selected.X));posY.Value=Math.Max(0,Math.Min(posY.Maximum,selected.Y));sizeW.Value=Math.Max(sizeW.Minimum,Math.Min(sizeW.Maximum,selected.Width));sizeH.Value=Math.Max(sizeH.Minimum,Math.Min(sizeH.Maximum,selected.Height));
                fontSize.Value=Math.Max(fontSize.Minimum,Math.Min(fontSize.Maximum,selected.FontSize));alignBox.SelectedItem=selected.Align??"Left";bold.Checked=selected.Bold;visible.Checked=selected.Visible;useFill.Checked=selected.UseFill;
                fitBox.SelectedItem=selected.Fit??"Cover";shapeBox.SelectedItem=selected.Shape??"Rectangle";
                var itemColor=Color.FromArgb(unchecked((int)(0xFF000000u|(uint)selected.Color)));foregroundColor.BackColor=itemColor;foregroundColor.ForeColor=Contrast(itemColor);
                var inside=Color.FromArgb(unchecked((int)(0xFF000000u|(uint)selected.FillColor)));fillColor.BackColor=inside;fillColor.ForeColor=Contrast(inside);
                bool textField=selected.Type=="Text"||selected.Type=="Gauge"||selected.Type=="Date";widgetText.ReadOnly=!textField;
                widgetImage.Visible=selected.Type=="Image";fitBox.Visible=selected.Type=="Image";shapeBox.Visible=selected.Type=="Shape";
                useFill.Visible=selected.Type=="Shape"||selected.Type=="Gauge"||selected.Type=="CPU"||selected.Type=="RAM"||selected.Type=="GPU";
            }
            loading=false;
        }
        void RefreshFrame() {if(canvasView.IsDisposed)return;canvasView.UpdateFrame();coordinates.Text=(landscape?"480 × 320":"320 × 480")+" px"+(selected==null?"":"   •   "+selected.X+", "+selected.Y);}
        void ChooseWidgetColor(object sender,EventArgs e) {if(selected==null)return;using(var dialog=new ColorDialog{Color=foregroundColor.BackColor,FullOpen=true})if(dialog.ShowDialog(this)==DialogResult.OK){RecordUndo();selected.Color=dialog.Color.ToArgb();RefreshInspector();RefreshFrame();}}
        void ChooseFillColor(object sender,EventArgs e) {if(selected==null)return;using(var dialog=new ColorDialog{Color=fillColor.BackColor,FullOpen=true})if(dialog.ShowDialog(this)==DialogResult.OK){RecordUndo();selected.FillColor=dialog.Color.ToArgb();RefreshInspector();RefreshFrame();}}
        void ChooseBackgroundColor(object sender,EventArgs e) {using(var dialog=new ColorDialog{Color=backgroundColor.BackColor,FullOpen=true})if(dialog.ShowDialog(this)==DialogResult.OK){RecordUndo();ActiveCanvas.Background=dialog.Color.ToArgb();draft.Background=ActiveCanvas.Background;RefreshInspector();RefreshFrame();}}
        void ChooseWidgetImage(object sender,EventArgs e) {
            if(selected==null||selected.Type!="Image")return;
            using(var dialog=new OpenFileDialog{Title="Trocar imagem do elemento",Filter="Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif"})if(dialog.ShowDialog(this)==DialogResult.OK)
                try{RecordUndo();selected.Text=ThemeStore.StoreImage(dialog.FileName);widgetText.Text=selected.Text;RefreshFrame();}
                catch(Exception ex){MessageBox.Show(this,ex.Message,"Imagem do elemento",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        void ApplyPalette(object sender,EventArgs e) {
            if(loading||palette.SelectedItem==null)return;string value=palette.SelectedItem.ToString();if(value=="Personalizada")return;
            RecordUndo();draft.Name=value;loading=true;themeName.Text=value;loading=false;
            if(value=="Grafite"){draft.Background=0x0016191F;draft.Foreground=0x00F2F3F5;draft.Muted=0x00B6BEC9;draft.CpuAccent=0x0058C4DB;draft.RamAccent=0x008BD49B;draft.GpuAccent=0x00F4B75C;draft.WeatherAccent=0x0087A8FF;}
            else if(value=="Amanhecer"){draft.Background=0x00211A30;draft.Foreground=0x00FFF7F0;draft.Muted=0x00E1C9D9;draft.CpuAccent=0x00FF92B4;draft.RamAccent=0x0090D8C3;draft.GpuAccent=0x00FFCA79;draft.WeatherAccent=0x00B6A0FF;}
            else{draft.Background=0x000F1D2B;draft.Foreground=0x00FFFFFF;draft.Muted=0x00B4CADC;draft.CpuAccent=0x0067CCE6;draft.RamAccent=0x006FD7A8;draft.GpuAccent=0x00F4BE68;draft.WeatherAccent=0x0067CCE6;}
            draft.Portrait.Background=draft.Background;draft.Landscape.Background=draft.Background;RefreshInspector();RefreshFrame();
        }
        void ChooseBackground(object sender,EventArgs e) {
            using(var dialog=new OpenFileDialog {Title="Escolha uma imagem para o fundo",Filter="Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif"})if(dialog.ShowDialog(this)==DialogResult.OK) {
                try {RecordUndo();ActiveCanvas.BackgroundImage=ThemeStore.StoreImage(dialog.FileName);RefreshInspector();RefreshFrame();}
                catch(Exception ex){MessageBox.Show(this,ex.Message,"Imagem de fundo",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        }
        void ExportTheme(object sender,EventArgs e) {
            using(var dialog=new SaveFileDialog{Title="Exportar tema com imagens",Filter="Pacote de tema|*.tema",FileName=(draft.Name??"Meu tema")+".tema"})if(dialog.ShowDialog(this)==DialogResult.OK)
                try{ThemeStore.Export(draft,dialog.FileName);MessageBox.Show(this,"Tema exportado com as imagens de fundo e dos elementos.","Tema exportado",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception ex){MessageBox.Show(this,"Não foi possível exportar: "+ex.Message,"Exportar tema",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        void ImportTheme(object sender,EventArgs e) {
            using(var dialog=new OpenFileDialog{Title="Importar tema",Filter="Pacote de tema|*.tema;*.zip"})if(dialog.ShowDialog(this)==DialogResult.OK)
                try {RecordUndo();draft=ThemeStore.Import(dialog.FileName);landscape=String.Equals(draft.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase);themeName.Text=draft.Name;seconds.Checked=draft.ShowSeconds;globalFont.SelectedItem=draft.Font;SetOrientation(landscape);RebuildLayers(null);RefreshFrame();}
                catch(Exception ex){MessageBox.Show(this,"Não foi possível abrir o pacote: "+ex.Message,"Importar tema",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        void SaveTheme(object sender,EventArgs e) {
            try {draft.Name=String.IsNullOrWhiteSpace(themeName.Text)?"Meu tema":themeName.Text.Trim();draft.Orientation=landscape?"Landscape":"Portrait";draft.ShowSeconds=seconds.Checked;ThemeStore.Save(draft);saved(draft.Clone());DialogResult=DialogResult.OK;Close();}
            catch(Exception ex){MessageBox.Show(this,"Não foi possível salvar o tema: "+ex.Message,"Salvar tema",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        void RecordUndo() {if(loading)return;undo.Push(draft.Clone());redo.Clear();UpdateHistoryButtons();}
        void UndoChange() {if(undo.Count==0)return;redo.Push(draft.Clone());draft=undo.Pop();SyncAfterHistory();}
        void RedoChange() {if(redo.Count==0)return;undo.Push(draft.Clone());draft=redo.Pop();SyncAfterHistory();}
        void SyncAfterHistory() {
            landscape=String.Equals(draft.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase);loading=true;themeName.Text=draft.Name;seconds.Checked=draft.ShowSeconds;globalFont.SelectedItem=draft.Font;loading=false;
            UpdateModeButtons();RebuildLayers(selected==null?null:selected.Id);RefreshFrame();UpdateHistoryButtons();
        }
        void UpdateHistoryButtons() {undoButton.Enabled=undo.Count>0;redoButton.Enabled=redo.Count>0;}
        void HandleKeyboard(object sender,KeyEventArgs e) {
            if(e.Control&&e.KeyCode==Keys.Z){UndoChange();e.Handled=true;return;}if(e.Control&&e.KeyCode==Keys.Y){RedoChange();e.Handled=true;return;}
            if(e.Control&&e.KeyCode==Keys.D){DuplicateSelected();e.Handled=true;return;}if(e.KeyCode==Keys.Delete&&selected!=null){DeleteSelected();e.Handled=true;return;}
            if(selected!=null&&(e.KeyCode==Keys.Left||e.KeyCode==Keys.Right||e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)) {
                RecordUndo();int step=e.Shift?8:1;if(e.KeyCode==Keys.Left)selected.X=Math.Max(0,selected.X-step);if(e.KeyCode==Keys.Right)selected.X=Math.Min(Panel.Width(draft)-selected.Width,selected.X+step);
                if(e.KeyCode==Keys.Up)selected.Y=Math.Max(0,selected.Y-step);if(e.KeyCode==Keys.Down)selected.Y=Math.Min(Panel.Height(draft)-selected.Height,selected.Y+step);
                RefreshInspector();RefreshFrame();layers.Invalidate();e.Handled=true;
            }
        }
        static Color Contrast(Color color) {return color.GetBrightness()<0.48f?Color.White:Color.FromArgb(18,28,43);}
        Button MakeButton(string text,Size size,Color? accentColor=null,bool filled=false) {
            var button=new Button {Text=text,Size=size,FlatStyle=FlatStyle.Flat,BackColor=filled?(accentColor??cyan):panelRaised,ForeColor=filled?page:ink,Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
            button.FlatAppearance.BorderColor=accentColor??Color.FromArgb(49,67,88);button.FlatAppearance.BorderSize=filled?0:1;return button;
        }
        void StyleText(TextBox box) {box.BackColor=panelRaised;box.ForeColor=ink;box.BorderStyle=BorderStyle.FixedSingle;box.Font=new Font("Segoe UI",9);}
        void StyleCombo(ComboBox box) {box.BackColor=panelRaised;box.ForeColor=ink;box.FlatStyle=FlatStyle.Flat;box.Font=new Font("Segoe UI",8.5f);}
        void StyleNumber(NumericUpDown number) {number.BackColor=panelRaised;number.ForeColor=ink;number.BorderStyle=BorderStyle.FixedSingle;number.Font=new Font("Consolas",9);}
        void StyleColorButton(Button button) {button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(62,82,105);button.BackColor=panelRaised;button.ForeColor=ink;button.Font=new Font("Segoe UI",8);}

        sealed class ThemeCanvasView : Control {
            Bitmap frame;RectangleF screen;bool moving,resizing;Point startMouse;int startX,startY,startW,startH;ThemeWidget dragItem;
            public Func<Bitmap> GetFrame;public Func<ThemeConfig> GetTheme;public Func<ThemeWidget> GetSelected;
            public bool ShowGrid=true,Snap=true;public event Action<ThemeWidget> SelectionChanged,DragStarted;public event Action<ThemeWidget> PositionChanged;
            public ThemeCanvasView() {SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Default;}
            public void UpdateFrame() {Bitmap next=GetFrame==null?null:GetFrame();Bitmap old=frame;frame=next;if(old!=null)old.Dispose();Invalidate();}
            public void DisposeFrame() {if(frame!=null){frame.Dispose();frame=null;}}
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e);Graphics g=e.Graphics;g.Clear(BackColor);
                using(var gridPen=new Pen(Color.FromArgb(23,119,149,170)))for(int x=0;x<Width;x+=24)g.DrawLine(gridPen,x,0,x,Height);
                using(var gridPen=new Pen(Color.FromArgb(23,119,149,170)))for(int y=0;y<Height;y+=24)g.DrawLine(gridPen,0,y,Width,y);
                ThemeConfig theme=GetTheme==null?new ThemeConfig():GetTheme();int w=Panel.Width(theme),h=Panel.Height(theme);
                float scale=Math.Min(Math.Max(1,Width-70)/(float)w,Math.Max(1,Height-76)/(float)h);float sw=w*scale,sh=h*scale;
                screen=new RectangleF((Width-sw)/2f,(Height-sh)/2f,sw,sh);
                using(var shadow=new SolidBrush(Color.FromArgb(95,0,0,0)))g.FillRectangle(shadow,screen.X+8,screen.Y+10,screen.Width,screen.Height);
                if(frame!=null)g.DrawImage(frame,screen);
                using(var border=new Pen(Color.FromArgb(104,142,166),1))g.DrawRectangle(border,screen.X,screen.Y,screen.Width,screen.Height);
                if(ShowGrid&&scale>=0.55f) {
                    using(var lines=new Pen(Color.FromArgb(31,210,230,238),1)) {
                        for(int x=8;x<w;x+=8){float px=screen.X+x*scale;if((x%32)==0)g.DrawLine(lines,px,screen.Y,px,screen.Bottom);}
                        for(int y=8;y<h;y+=8){float py=screen.Y+y*scale;if((y%32)==0)g.DrawLine(lines,screen.X,py,screen.Right,py);}
                    }
                }
                ThemeWidget selected=GetSelected==null?null:GetSelected();if(selected!=null) {
                    RectangleF rect=new RectangleF(screen.X+selected.X*scale,screen.Y+selected.Y*scale,selected.Width*scale,selected.Height*scale);
                    using(var outline=new Pen(Color.FromArgb(235,255,180,84),2)){outline.DashPattern=new float[]{5,3};g.DrawRectangle(outline,rect.X,rect.Y,rect.Width,rect.Height);}
                    float handle=Math.Max(8,Math.Min(13,scale*9));using(var fill=new SolidBrush(Color.FromArgb(255,180,84)))g.FillRectangle(fill,rect.Right-handle/2,rect.Bottom-handle/2,handle,handle);
                    string label=selected.X+", "+selected.Y+"  /  "+selected.Width+" × "+selected.Height+" px";
                    using(var font=new Font("Consolas",8,FontStyle.Bold))using(var brush=new SolidBrush(Color.FromArgb(255,180,84)))g.DrawString(label,font,brush,rect.Left,Math.Max(4,rect.Top-19));
                }
                using(var font=new Font("Consolas",8))using(var brush=new SolidBrush(Color.FromArgb(130,171,191)))g.DrawString(w+" × "+h+" px",font,brush,screen.Left,screen.Bottom+8);
            }
            PointF ToCanvas(Point point) {float sx=screen.Width<=0?1:screen.Width/Panel.Width(GetTheme());float sy=screen.Height<=0?1:screen.Height/Panel.Height(GetTheme());return new PointF((point.X-screen.X)/sx,(point.Y-screen.Y)/sy);}
            bool historyCaptured;
            protected override void OnMouseDown(MouseEventArgs e) {
                base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;ThemeConfig theme=GetTheme();ThemeWidget old=GetSelected();PointF p=ToCanvas(e.Location);
                if(!screen.Contains(e.Location)){if(old!=null)SelectionChanged(null);return;}
                ThemeCanvas active=theme.Canvas(String.Equals(theme.Orientation,"Landscape",StringComparison.OrdinalIgnoreCase));ThemeWidget hit=null;bool handleHit=false;
                if(old!=null) {
                    float hx=screen.X+(old.X+old.Width)*screen.Width/Panel.Width(theme);float hy=screen.Y+(old.Y+old.Height)*screen.Height/Panel.Height(theme);
                    handleHit=Math.Abs(e.X-hx)<12&&Math.Abs(e.Y-hy)<12;if(handleHit)hit=old;
                }
                if(hit==null)for(int i=active.Widgets.Count-1;i>=0;i--){var item=active.Widgets[i];if(item!=null&&item.Visible&&new Rectangle(item.X,item.Y,item.Width,item.Height).Contains((int)p.X,(int)p.Y)){hit=item;break;}}
                if(hit!=null) {
                    if(old==null||old.Id!=hit.Id)SelectionChanged(hit);
                    dragItem=hit;startMouse=e.Location;startX=hit.X;startY=hit.Y;startW=hit.Width;startH=hit.Height;
                    PointF selectedBottomRight=new PointF(screen.X+(hit.X+hit.Width)*screen.Width/Panel.Width(theme),screen.Y+(hit.Y+hit.Height)*screen.Height/Panel.Height(theme));
                    resizing=handleHit||Math.Abs(e.X-selectedBottomRight.X)<12&&Math.Abs(e.Y-selectedBottomRight.Y)<12;moving=!resizing;historyCaptured=false;Capture=true;
                } else if(old!=null) { SelectionChanged(null); }
            }
            protected override void OnMouseMove(MouseEventArgs e) {
                base.OnMouseMove(e);ThemeWidget selected=GetSelected();
                if(!moving&&!resizing) {
                    if(selected!=null) {float hx=screen.X+(selected.X+selected.Width)*screen.Width/Panel.Width(GetTheme());float hy=screen.Y+(selected.Y+selected.Height)*screen.Height/Panel.Height(GetTheme());if(Math.Abs(e.X-hx)<12&&Math.Abs(e.Y-hy)<12){Cursor=Cursors.SizeNWSE;return;}}
                    Cursor=screen.Contains(e.Location)?Cursors.Hand:Cursors.Default;return;
                }
                if(dragItem==null)return;if(!historyCaptured){DragStarted(dragItem);historyCaptured=true;}PointF delta=ToCanvas(e.Location);PointF origin=ToCanvas(startMouse);int dx=(int)Math.Round(delta.X-origin.X),dy=(int)Math.Round(delta.Y-origin.Y);
                if(Snap){dx=(int)Math.Round(dx/8.0)*8;dy=(int)Math.Round(dy/8.0)*8;}
                ThemeConfig theme=GetTheme();int maxW=Panel.Width(theme),maxH=Panel.Height(theme);
                if(moving){dragItem.X=Math.Max(0,Math.Min(maxW-dragItem.Width,startX+dx));dragItem.Y=Math.Max(0,Math.Min(maxH-dragItem.Height,startY+dy));}
                else {dragItem.Width=Math.Max(16,Math.Min(maxW-startX,startW+dx));dragItem.Height=Math.Max(16,Math.Min(maxH-startY,startH+dy));}
                PositionChanged(dragItem);Invalidate();
            }
            protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);moving=false;resizing=false;dragItem=null;historyCaptured=false;Capture=false;}
        }
    }
}
