using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Threading.Tasks;
using BooleanMatch.Properties;
using System.Drawing;

namespace BooleanMatch
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();

            UnlockUI();
        }

        private string path;
        private static List<bool> match = new List<bool>();

        CancellationTokenSource cts;


        private void btnSearch_Click(object sender, EventArgs e)
        {
            if (cts != null)
            {
                cts.Cancel();
            }
            else
            {
                SearchForMatchAsync();
            }
        }

        void DoMatch(string s)
        {
            match.Clear();
            for (int i = 0; i < s.Length; i++)
            {
                for (int j = i + 1; j < s.Length; j++)
                {
                    match.Add(s[i] == s[j]);
                }
            }
        }

        void LockUI()
        {
            txtFileName.Enabled = false;
            btnFileOpen.Enabled = false;
            radioButton1.Enabled = false;
            radioButton2.Enabled = false;
            textBox1.Enabled = false;
            btnSearch.Image = (Bitmap)Resources.ResourceManager.GetObject("cross");
        }

        void UnlockUI()
        {
            txtFileName.Enabled = true;
            btnFileOpen.Enabled = true;
            radioButton1.Enabled = true;
            radioButton2.Enabled = true;
            textBox1.Enabled = true;
            btnSearch.Image = (Bitmap)Resources.ResourceManager.GetObject("magnifier");
        }


        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            listView2.Items.Clear();

            if (listView1.SelectedItems.Count > 0)
            {
                SortedDictionary<char, string> table = new SortedDictionary<char, string>(((Match)listView1.SelectedItems[0].Tag).table);

                foreach (KeyValuePair<char, string> kvp in table)
                {
                    listView2.Items.Add(new ListViewItem(new string[] { "0x" + kvp.Value.ToString(), kvp.Key.ToString() }));
                }
            }

        }

        private void listView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && listView1.SelectedItems.Count > 0)
                contextMenu.Show((ListView)sender, e.Location);
        }

        private void saveTable_MenuItem_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog ofd = new SaveFileDialog())
            {
                ofd.FileName = Path.GetFileNameWithoutExtension(path) + "_0x" + ((Match)listView1.SelectedItems[0].Tag).offset.ToString("X") + "_" + (radioButton1.Checked ? 8 : 16) + "bits";
                ofd.Filter = "Table File|*.tbl";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string s = String.Empty;

                    SortedDictionary<char, string> table = new SortedDictionary<char, string>(((Match)listView1.SelectedItems[0].Tag).table);
                    foreach (KeyValuePair<char, string> kvp in table)
                    {
                        s += kvp.Value.ToString() + "=" + kvp.Key.ToString() + "\r\n";
                    }

                    using (StreamWriter sw = new StreamWriter(ofd.FileName))
                    {
                        sw.Write(s);
                    }
                }
            }
        }

        private void copyOffset_MenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(((Match)listView1.SelectedItems[0].Tag).offset.ToString("X"));
        }

        private void btnFileOpen_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Multiselect = false;
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtFileName.Text = ofd.FileName;
                    UnlockUI();
                }
            }
        }

        private async Task SearchForMatchAsync()
        {
            path = txtFileName.Text;
            string inputString = textBox1.Text;
            if (!File.Exists(path))
            {
                MessageBox.Show("File does not exists!");
                return;
            }

            DoMatch(inputString);

            int sum = 0;
            foreach (bool b in match)
            {
                sum += b ? 1 : 0;
            }

            if (sum == 0 || sum == match.Count || sum < 3)
            {
                MessageBox.Show("Invalid input string!");
                return;
            }

            try
            {
                listView1.ListViewItemSorter = null;
                listView1.Items.Clear();
                listView2.Items.Clear();

                cts = new CancellationTokenSource();
                ParallelOptions po = new ParallelOptions
                {
                    CancellationToken = cts.Token,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                LockUI();

                await Task.Run(() =>
                {
                    int type = radioButton1.Checked ? 1 : 2;
                    

                    using (FileStream fs = new FileStream(path, FileMode.Open))
                    {
                        uint bufferSize = 0x4000000; //4MB 0x8000000
                        byte[] b = new byte[bufferSize];
                        int loop = (int)Math.Ceiling(fs.Length / (decimal)bufferSize);
                        long pos;

                        progressBar1.Invoke(new Action(() =>
                        {
                            progressBar1.Value = 0;
                            progressBar1.Maximum = loop;
                        }));

                        for (int l = 0; l < loop; l++)
                        {
                            if (l == loop - 1)
                                b = new byte[fs.Length - fs.Position];

                            pos = fs.Position;
                            fs.Read(b, 0, b.Length);

                            Parallel.For(0, (b.Length - inputString.Length * type), po, i => SearchForMatch(b, i, inputString, type, pos, po));

                            fs.Position -= inputString.Length * type;

                            progressBar1.BeginInvoke(new Action(() =>
                            {
                                progressBar1.Increment(1);
                            }));
                        }
                    }

                });
            }
            finally
            {
                cts.Dispose();
                cts = null;
                GC.Collect();

                MessageBox.Show("Search completed!");
                UnlockUI();
            }
        }

        protected void SearchForMatch(byte[] b, int x, string find, int type, long pos, ParallelOptions po)
        {
            int matchCounter = 0;
            bool arrayMatch = true;
            for (int i = x; i < x + find.Length * type && arrayMatch; i += type)
            {
                for (int j = i + type; j < x + find.Length * type && arrayMatch; j += type)
                {
                    arrayMatch = (match[matchCounter] == (b[i] == b[j]));
                    if (arrayMatch) ++matchCounter;
                }
            }

            if (matchCounter == match.Count)
            {
                MatchFound(pos + x, ref x, ref find, ref b, ref type);
            }
            po.CancellationToken.ThrowIfCancellationRequested();
        }


        private void MatchFound(long mOffset, ref int pos, ref string t, ref byte[] b, ref int type)
        {
            Match m = new Match()
            {
                offset = mOffset
            };

            char k;
            string v;

            string s = String.Empty;
            for (int i = 0; i < t.Length * type; i += type)
            {
                k = t[i / type];
                if (!m.table.ContainsKey(k))
                {
                    v = b[pos + i].ToString("X2") + (type == 2 ? b[pos + i + 1].ToString("X2") : "");
                    s += string.Format("{0}={1}; ", v, k);
                    m.table.Add(k, v);
                }
            }

            listView1.BeginInvoke(new Action(() =>
            {
                ListViewItem lvi = new ListViewItem(new string[] { "0x" + mOffset.ToString("X"), s })
                {
                    Tag = m
                };

                listView1.Items.Add(lvi);

            }));
        }

        private void Form1_HelpButtonClicked(object sender, CancelEventArgs e)
        {
            using (About about = new About())
            {
                about.ShowDialog();
            }
            e.Cancel = true;
        }
    }

}

