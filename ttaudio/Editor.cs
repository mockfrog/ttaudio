// Copyright (c) https://github.com/sidiandi 2016
// 
// This file is part of tta.
// 
// tta is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// tta is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Foobar.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using log4net.Layout;
using log4net.Core;
using RavSoft;
using ttaenc;

namespace ttaudio
{
    public partial class Editor : Form
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        readonly Document document;

        public Editor(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            this.document = document;

            InitializeComponent();

            this.listViewInputFiles.Columns.Clear();
            var mWidth = (int) listViewInputFiles.Font.Size;
            this.listViewInputFiles.Columns.AddRange(new[] {
                    new ColumnHeader { Text = "Oid", Width = 5 * mWidth },
                    new ColumnHeader { Text = "Artist", Width = 16 * mWidth },
                    new ColumnHeader { Text = "Album", Width = 16 * mWidth},
                    new ColumnHeader { Text = "#", Width = 5 * mWidth},
                    new ColumnHeader { Text = "Title", Width = 32 * mWidth},
            });

            CueProvider.SetCue(textBoxTitle, "automatic");
            CueProvider.SetCue(textBoxProductId, "automatic");

            UpdateView();
        }

        public static Editor Open(string file)
        {
            var e = new Editor(Document.Load(file));
            e.Show();
            return e;
        }

        private void listViewInputFiles_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                Add(files);
            }
        }

        /// <summary>
        /// Add input files to the list view
        /// </summary>
        /// <param name="inputFiles"></param>
        public async Task Add(IEnumerable<string> inputFiles)
        {
            await TaskForm.StartTask("Add Files", () => this.document.package.AddTracks(inputFiles));
            UpdateView();
        }

        private void listViewInputFiles_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void buttonConvert_Click(object sender, EventArgs e)
        {
            StartConversion();
        }

        Task Convert(CancellationToken cancel, IList<string> files, string title, string productId)
        {
            return Task.Factory.StartNew(async () => 
            {
                try
                {
                    var package = Package.CreateFromInputPaths(files);
                    
                    if (!String.IsNullOrEmpty(title))
                    {
                        package.Title = title;
                    }
                    if (!String.IsNullOrEmpty(productId))
                    {
                        package.ProductId = UInt16.Parse(productId);
                    }

                    var converter = Context.GetDefaultMediaFileConverter();

                    var pen = TipToiPen.GetAll().First();
                    var packageBuilder = new PackageBuilder(
                        new PackageDirectoryStructure(pen.RootDirectory, package), converter);

                    await packageBuilder.Build(cancel);
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            }, cancel);
        }

        void StartConversion()
        {
            UpdateModel();

            var files = listViewInputFiles.Items.Cast<ListViewItem>().Select(_ => (string)_.Tag).ToList();
            if (!files.Any())
            {
                MessageBox.Show("Drop some audio files into the list first.");
                return;
            }

            var cancellationTokenSource = new System.Threading.CancellationTokenSource();
            var task = Convert(cancellationTokenSource.Token, files, textBoxTitle.Text, textBoxProductId.Text);

            var taskForm = new TaskForm(task, cancellationTokenSource)
            {
                Text = "Convert and Copy to Pen"
            };
                
            taskForm.Show();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowAboutInformation();
        }

        private void exploreDataDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Process.Start("explorer.exe", AlbumMaker.GetDefaultDataDirectory().Quote());
            /*if (platform.OS.IsUnix)
            {
                string datadir = AlbumMaker.GetDefaultDataDirectory();
                PathUtil.EnsureDirectoryExists(datadir);
                Process.Start("xdg-open", datadir.Quote());
            }
            else
            {
                Process.Start("explorer.exe", AlbumMaker.GetDefaultDataDirectory().Quote());
            }*/
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        void DeleteSelected()
        {
            document.package.RemoveTracks(listViewInputFiles.Items.Cast<ListViewItem>().Where(_ => _.Selected)
                .Select(_ => (Track)_.Tag));
            UpdateView();
        }

        void SelectAll()
        {
            foreach (ListViewItem i in listViewInputFiles.Items)
            {
                i.Selected = true;
            }
        }

        private void listViewInputFiles_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers == Keys.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.A:
                        SelectAll();
                        break;
                }
            }
            else
            {
                switch (e.KeyCode)
                {
                    case Keys.Delete:
                        DeleteSelected();
                        break;
                }
            }
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Save();
        }

        void Save()
        {
            if (document.ttaFile == null)
            {
                SaveAs();
                return;
            }

            UpdateModel();
            document.Save();
        }

        const string fileDialogFilter = "TipToi Game Files (*.gme)|*.gme";

        void SaveAs()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.InitialDirectory = ttaenc.About.DocumentsDirectory;
            saveFileDialog.Filter = Document.fileDialogFilter;
            if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                document.ttaFile = saveFileDialog.FileName;
            }
            Save();
        }

        void UpdateView()
        {
            Package p;
            lock (this)
            {
                p = document.package;
            }

            listViewInputFiles.Items.Clear();
            listViewInputFiles.Items.AddRange(p.Tracks.Select(track => new ListViewItem(new string[] {
                track.Oid.ToString(),
                String.Join(", ", track.Artists),
                track.Album,
                track.TrackNumber.ToString(),
                track.Title
            })
            {
                Tag = track
            }).ToArray());

            this.textBoxTitle.Text = p.Title;
            if (p.ProductId != 0)
            {
                this.textBoxProductId.Text = p.ProductId.ToString();
            }

            this.Text = String.Join(" - ", new string[] { About.Product, this.document.ttaFile }
                .Where(_ => !String.IsNullOrEmpty(_)));
        }

        void UpdateModel()
        {
            lock (this)
            {
                var p = document.package;
                p.Title = this.textBoxTitle.Text;
                int productId;
                if (Int32.TryParse(this.textBoxProductId.Text, out productId))
                {
                    p.ProductId = productId;
                }
            }
        }

        private void printToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Print();
        }

        public void Print()
        {
            var cts = new CancellationTokenSource();
            var task = Task.Factory.StartNew(() =>
            {
                UpdateModel();
                var builder = GetPackageBuilder();
                builder.OpenHtmlPage(cts.Token);
            }, TaskCreationOptions.LongRunning);
            var f = new TaskForm(task, cts) { Text = "Print" };
            f.Show();
        }

        public void Upload()
        {
            var cts = new CancellationTokenSource();
            var task = Task.Factory.StartNew(() =>
            {
                UpdateModel();
                var builder = GetPackageBuilder();
                builder.Build(cts.Token);
            }, TaskCreationOptions.LongRunning);

            var f = new TaskForm(task, cts) { Text = "Upload" };
            f.Show();
        }

        PackageBuilder GetPackageBuilder()
        {
            var s = new PackageDirectoryStructure(GetRootDirectory(), this.document.package);
            return new PackageBuilder(s, Context.GetDefaultMediaFileConverter());
        }

        public static string GetRootDirectory()
        {
            return TipToiPen.Get().RootDirectory;
        }

        private void uploadToPenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Upload();
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            Upload();
        }

        private void aboutToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            ShowAboutInformation();
        }

        public void ShowAboutInformation()
        {
            ttaenc.OS.OpenHtmlFile(ttaenc.About.GithubUri.ToString());
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            InstanceOpen();
        }

        public static void Open()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                InitialDirectory = About.DocumentsDirectory,
            };
            PathUtil.EnsureDirectoryExists(openFileDialog.InitialDirectory);

            openFileDialog.Filter = Document.fileDialogFilter;
            if (openFileDialog.ShowDialog(null) == DialogResult.OK)
            {
                Open(openFileDialog.FileName);
            }
        }

        public void InstanceOpen()
        {
            Open();
            CloseIfEmpty();
        }

        public void InstanceNew()
        {
            New();
            CloseIfEmpty();
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            InstanceNew();
        }

        void CloseIfEmpty()
        {
            if (!document.package.Tracks.Any())
            {
                Close();
            }
        }

        public static Editor New()
        {
            var e = new Editor(new Document());
            e.Show();
            return e;
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void printerTestPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Task.Factory.StartNew(() =>
            {
                var testPage = Path.Combine(About.LocalApplicationDataDirectory, "tiptoi-printer-test.html");
                PathUtil.EnsureParentDirectoryExists(testPage);
                OidSvgWriter.CreatePrinterTestPage(testPage);
                OS.OpenHtmlFile(testPage);
            }, TaskCreationOptions.LongRunning);
        }

        private void printToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            Print();
        }

        private void uploadToPenToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            Upload();
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            Upload();
        }

        private void toolStripButton3_Click(object sender, EventArgs e)
        {
            Print();
        }

        private void Editor_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (Application.OpenForms.Count == 0)
            {
                Application.Exit();
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveAs();
        }
    }
}
