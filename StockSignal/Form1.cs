using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace StockSignal
{
    // https://github.com/zero5432/StockSignal.git

    /// 
    /// EMA1=12, EMA2=26 の値を色々変えてみたけど、MACDを元にした勝率は特に変わらなかった
    ///

#if false
    ％を変更した結果の上位

    利  損  収益
    --------------
    10	5	12900
	9	5	11640
	9	8	11380
	10	7	10760
	7	5	10740
	10	2	10710
	10	4	10570
	7	1	10390
	7	2	10380
	9	2	10190
	9	9	10020
	8	5	9540
	9	7	9500
	7	7	9470
	9	4	9310
	10	3	9230

#endif

    public partial class Form1 : Form
    {
        const int Ave20Max = 20;

        // 全体データ
        DataTable wholeData = new DataTable();

        // 期間データ
        DataTable periodData = new DataTable();

        DateTime dtFrom = DateTime.MaxValue;
        DateTime dtTo = DateTime.MinValue;

        /// <summary>
        /// グラフが描画された
        /// </summary>
        bool isDraw = false;

        enum keyWd
        {
            DATE,
            START,
            END,
            MIN,
            MAX,
            VOLUME,
            PRICE,
            EMA12,
            EMA26,
            EMA8,
            SMA20,      // 20日移動平均
            BB_PLUS1,   // ボリンジャーバンド ＋1シグマ
            BB_PLUS2,   // ボリンジャーバンド ＋2シグマ
            BB_MINUS1,  // ボリンジャーバンド －1シグマ
            BB_MINUS2,  // ボリンジャーバンド －2シグマ
            MACD,
            MACD_SIG,
            SIGNAL,
            //SIGNAL_POINT_BUY_1,         // グラフ1 買いシグナル
            //SIGNAL_POINT_SELL_1,        // グラフ1 売りシグナル
            SIGNAL_POINT_BUY_1_WIN,     // グラフ1 買いシグナル 勝ち
            SIGNAL_POINT_BUY_1_LOSE,    // グラフ1 買いシグナル 負け
            SIGNAL_POINT_SELL_1_WIN,    // グラフ1 売りシグナル 勝ち
            SIGNAL_POINT_SELL_1_LOSE,   // グラフ1 売りシグナル 勝ち
            SIGNAL_POINT_BUY_2,
            SIGNAL_POINT_SELL_2,
        };

        public Form1()
        {
            InitializeComponent();
        }

        private void buttonClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private string GetEnumName(keyWd key)
        {
            return Enum.GetName(typeof(keyWd), key);
        }

        private void buttonRun_Click(object sender, EventArgs e)
        {
            try
            {
                // 指定 EMA で全データ算出
                CalcWholeDataByEMA((int)this.numericUpDown1.Value, (int)this.numericUpDown2.Value);
#if false
                // 8日指数平滑平均を出力
                using (StreamWriter sw = new StreamWriter(@"D:\Down\n22502015_out.csv", false, Encoding.GetEncoding("shift-jis")))
                {
                    int prev = 0;
                    foreach (DataRow r in this.wholeData.Rows)
                    {
                        int cur = r.Field<int>(GetEnumName(keyWd.EMA8));
                        sw.WriteLine(string.Format("{0},{1},{2}", (r.Field<DateTime>(GetEnumName(keyWd.DATE))).ToString("yyyy/MM/dd"), cur, cur - prev));
                        prev = cur;
                    }
                }
#endif
                // 日にち指定した DataTable 生成
                CreatePeriodData(this.dateTimePicker1.Value, this.dateTimePicker2.Value);

                // DataTable 中の min, MAX 列の最大最小値取得
                DataView dv = new DataView(this.periodData, string.Format("{0} > 0", GetEnumName(keyWd.MAX)), GetEnumName(keyWd.MIN), DataViewRowState.Added);
                int priceMin = (int)((dv[0]).Row.ItemArray[(int)keyWd.MIN]);
                dv = new DataView(this.wholeData, string.Format("{0} > 0", GetEnumName(keyWd.MAX)), GetEnumName(keyWd.MAX), DataViewRowState.Added);
                int priceMax = (int)((dv[dv.Count - 1]).Row.ItemArray[(int)keyWd.MAX]);
                this.chart1.ChartAreas[0].AxisY.Minimum = (int)(Math.Floor((priceMin / 1000.0)) * 1000);
                this.chart1.ChartAreas[0].AxisY.Maximum = (int)(Math.Ceiling((priceMax / 1000.0)) * 1000);

                // 損益計算
                CalcProfit();

                CalcOptProfit();

                this.chart1.DataSource = this.periodData;
                this.chart1.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CalcOptProfit()
        {
            // DataGridView 表示
            // 必要項目を抽出した DataTable を生成し、DataGridView のソースにする
            DataRow[] dataRowAry = this.periodData.Select(string.Format("{0} <> ''", GetEnumName(keyWd.SIGNAL)));
            DataTable data4Grid = new DataTable();
            data4Grid.Columns.Add(GetEnumName(keyWd.SIGNAL), typeof(string));
            data4Grid.Columns.Add(GetEnumName(keyWd.DATE), typeof(DateTime));
            data4Grid.Columns.Add("取得価格", typeof(int));
            data4Grid.Columns.Add("利確日", typeof(DateTime));
            data4Grid.Columns.Add("損益", typeof(int));

            double rieki = (double)this.numericUpDownRieki.Value / 100.0;
            double sonsitu = (double)this.numericUpDownSon.Value / 100.0 * (-1.0);

            DataTable dataSonneki = new DataTable();
            dataSonneki.Columns.Add("+割合", typeof(double));
            dataSonneki.Columns.Add("-割合", typeof(double));
            dataSonneki.Columns.Add("損益", typeof(int));
            int sonneki = 0;
            for (int ri = 1; ri <= 10; ri += 1)
            {
                int tmpri = ri;
                for (int so = 1; so <= 10; so += 1)
                {
                    if (so > ri)
                    {
                        break;
                    }
                    double tmpso = so;
                    sonneki = 0;
                    TimeSpan span = TimeSpan.MinValue;
                    foreach (DataRow r in dataRowAry)
                    {
                        DataRow tmpr = data4Grid.NewRow();
                        tmpr[GetEnumName(keyWd.SIGNAL)] = r[GetEnumName(keyWd.SIGNAL)];
                        tmpr[GetEnumName(keyWd.DATE)] = r[GetEnumName(keyWd.DATE)];
                        tmpr["取得価格"] = r[GetEnumName(keyWd.END)];

                        sonneki += CalcOptTgtDayProfit(tmpr, tmpri, tmpso, ref span);
                    }
                    DataRow nr = dataSonneki.NewRow();
                    nr["+割合"] = tmpri;
                    nr["-割合"] = tmpso;
                    nr["損益"] = sonneki;
                    dataSonneki.Rows.Add(nr);
                }
            }
            dataSonneki.DefaultView.Sort = "損益 DESC";
            this.dataGridView3.DataSource = dataSonneki;
        }

        private int CalcOptTgtDayProfit(DataRow orgRow, double rieki, double sonsitu, ref TimeSpan span)
        {
            // 指定日以降のDataTbaleを取得 
            DateTime dttgtDate = (DateTime)(orgRow[GetEnumName(keyWd.DATE)]);
            string tgtDateStr = dttgtDate.ToString("yyyy/MM/dd 00:00:00");
            DataRow[] rowAry = this.wholeData.Select(string.Format("{0} >= '{1}'", GetEnumName(keyWd.DATE), tgtDateStr));
            if (rowAry.Length == 0)
            {
                MessageBox.Show("何か日付が変:{0}", tgtDateStr);
                return 0;
            }

            rieki /= 100.0;
            sonsitu /= 100.0 * (-1.0);
            int startPrice = (int)rowAry[0][GetEnumName(keyWd.START)];
            int sonneki = 0;
            bool buy = (string)rowAry[0][GetEnumName(keyWd.SIGNAL)] == "買い";
            keyWd winLose = keyWd.SIGNAL_POINT_BUY_1_WIN;
            DateTime dtEnd = DateTime.MinValue;
            for (int i = 1; i < rowAry.Length; i++)
            {
                int start = (int)rowAry[i][GetEnumName(keyWd.START)];
                int end = (int)rowAry[i][GetEnumName(keyWd.END)];
                int min = (int)rowAry[i][GetEnumName(keyWd.MIN)];
                int max = (int)rowAry[i][GetEnumName(keyWd.MAX)];
                double wariai = ((double)(end - startPrice)) / startPrice;
                sonneki = end - startPrice;
                if (buy == false)
                {
                    wariai *= -1.0;
                    sonneki *= -1;
                }

                if (wariai >= rieki || wariai <= sonsitu)
                {
                    if (wariai >= rieki)
                    {
                        winLose = buy == true ? keyWd.SIGNAL_POINT_BUY_1_WIN : keyWd.SIGNAL_POINT_SELL_1_WIN;
                    }
                    else
                    {
                        winLose = buy == true ? keyWd.SIGNAL_POINT_BUY_1_LOSE : keyWd.SIGNAL_POINT_SELL_1_LOSE;
                    }
                    dtEnd = (DateTime)rowAry[i][GetEnumName(keyWd.DATE)];
                    span = (dtEnd - dttgtDate);
                    if (span.Days > 100)
                    {
                        winLose = buy == true ? keyWd.SIGNAL_POINT_BUY_1_LOSE : keyWd.SIGNAL_POINT_SELL_1_LOSE;
                        if (sonneki > 0)
                        {
                            sonneki *= -1;
                        }
                    }
                    break;
                }
            }
            //string result = string.Format("{0}% {1}% {2}", rieki, sonsitu, sonneki);
            //this.periodData.Rows.Find(tgtDateStr)[GetEnumName(winLose)] = startPrice;

            //orgRow["利確日"] = dtEnd;
            //orgRow["損益"] = sonneki;
            return sonneki;
        }

        private void CalcProfit()
        {
            // DataGridView 表示
            // 必要項目を抽出した DataTable を生成し、DataGridView のソースにする
            DataRow[] dataRowAry = this.periodData.Select(string.Format("{0} <> ''", GetEnumName(keyWd.SIGNAL)));
            DataTable data4Grid = new DataTable();
            data4Grid.Columns.Add(GetEnumName(keyWd.SIGNAL), typeof(string));
            data4Grid.Columns.Add(GetEnumName(keyWd.DATE), typeof(DateTime));
            data4Grid.Columns.Add("取得価格", typeof(int));
            data4Grid.Columns.Add("span", typeof(int));
            data4Grid.Columns.Add("利確日", typeof(DateTime));
            data4Grid.Columns.Add("損益", typeof(int));

            foreach (DataRow r in dataRowAry)
            {
                DataRow tmpr = data4Grid.NewRow();
                tmpr[GetEnumName(keyWd.SIGNAL)] = r[GetEnumName(keyWd.SIGNAL)];
                tmpr[GetEnumName(keyWd.DATE)] = r[GetEnumName(keyWd.DATE)];
                tmpr["取得価格"] = r[GetEnumName(keyWd.END)];
                data4Grid.Rows.Add(tmpr);

                CalcTgtDayProfit(tmpr, data4Grid);
            }

            data4Grid.DefaultView.Sort = GetEnumName(keyWd.DATE);

            this.dataGridView1.DataSource = data4Grid;

            var kati = data4Grid.AsEnumerable()
                .Where(row => row.Field<int>("損益") > 0)
                .Select(row => row["損益"]).Count();
            var sum2 = data4Grid.AsEnumerable()
                .Select(row => row.Field<int>("損益")).Sum();

            foreach (DataGridViewRow dgRow in this.dataGridView1.Rows)
            {
                if ((int)dgRow.Cells["損益"].Value < 0)
                {
                    dgRow.DefaultCellStyle.BackColor = Color.Black;
                    dgRow.DefaultCellStyle.ForeColor = Color.Gray;
                }
            }
            this.textBoxTotal.Text = string.Format("{0}勝{1}敗 {2}", kati, data4Grid.Rows.Count - kati, sum2);

            this.isDraw = true;
        }

        /// <summary>
        /// 日にち指定した DataTable 生成
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        private void CreatePeriodData(DateTime from, DateTime to)
        {
            this.wholeData.DefaultView.RowFilter = string.Format("{0} >= '{1}' AND {0} <= '{2}'",
                GetEnumName(keyWd.DATE), from.ToString("yyyy/MM/dd"), to.ToString("yyyy/MM/dd"));
            this.periodData = this.wholeData.DefaultView.ToTable();
            this.periodData.PrimaryKey = new DataColumn [] { this.periodData.Columns[GetEnumName(keyWd.DATE)] };
        }

        /// <summary>
        /// 入力を元にして全体の DataTable を生成
        /// </summary>
        /// <param name="dataList"></param>
        private void CreateWholeData(List<Data> dataList)
        {
            int prevMacdSig = int.MinValue;
            int priceMin = int.MaxValue;
            int priceMax = int.MinValue;

            this.wholeData.Clear();

            foreach (keyWd key in Enum.GetValues(typeof(keyWd)))
            {
                if (key == keyWd.DATE)
                {
                    this.wholeData.Columns.Add(GetEnumName(key), typeof(DateTime));
                }
                else if (key == keyWd.SIGNAL)
                {
                    this.wholeData.Columns.Add(GetEnumName(key), typeof(string));
                }
                else
                {
                    this.wholeData.Columns.Add(GetEnumName(key), typeof(int));
                }
            }
            this.wholeData.PrimaryKey = new DataColumn[] { this.wholeData.Columns[GetEnumName(keyWd.DATE)] };

            Queue<int> ave20Queue = new Queue<int>(20);

            double alpha9 = GetAlpha(9);
            double alpha12 = GetAlpha((int)this.numericUpDown1.Value);
            double alpha26 = GetAlpha((int)this.numericUpDown2.Value);
            int prevEma12 = dataList[0].end;
            int prevEma26 = prevEma12;
            int prevEma8 = prevEma12;
            foreach (Data tmp in dataList)
            {
                DataRow r = this.wholeData.NewRow();
                DateTime dt = ConvertStringToDateTime(tmp.date);

                int todayEma12 = GetEMA(prevEma12, tmp.end, alpha12);
                prevEma12 = todayEma12;

                int todayEma26 = GetEMA(prevEma26, tmp.end, alpha26);
                prevEma26 = todayEma26;

                int todayMacd = todayEma12 - todayEma26;
                if (prevMacdSig == int.MinValue)
                {
                    prevMacdSig = todayMacd;
                }
                int todayMacdSig = GetEMA(prevMacdSig, todayMacd, alpha9);

                // 20日移動平均
                int ave20 = GetAve20(tmp.end, ave20Queue);

                // 標準偏差とボリンジャーバンド
                double stdev = GetStDev(ave20Queue);
                int bbp1 = ave20 + (int)stdev;
                int bbp2 = ave20 + (int)(stdev * 2);
                int bbm1 = ave20 - (int)stdev;
                int bbm2 = ave20 - (int)(stdev * 2);

                r[GetEnumName(keyWd.DATE)] = dt;
                r[GetEnumName(keyWd.START)] = tmp.start;
                r[GetEnumName(keyWd.END)] = tmp.end;
                r[GetEnumName(keyWd.MIN)] = tmp.min;
                r[GetEnumName(keyWd.MAX)] = tmp.max;
                r[GetEnumName(keyWd.VOLUME)] = tmp.volume;
                r[GetEnumName(keyWd.SMA20)] = ave20;
                r[GetEnumName(keyWd.BB_PLUS1)] = bbp1;
                r[GetEnumName(keyWd.BB_PLUS2)] = bbp2;
                r[GetEnumName(keyWd.BB_MINUS1)] = bbm1;
                r[GetEnumName(keyWd.BB_MINUS2)] = bbm2;


                this.wholeData.Rows.Add(r);
                priceMin = Math.Min(priceMin, tmp.min);
                priceMax = Math.Max(priceMax, tmp.max);
            }
        }

        /// <summary>
        /// 20日ぶんの標準偏差を求める
        /// 20日なければその分の標準偏差
        /// </summary>
        /// <param name="ave20Queue"></param>
        /// <returns></returns>
        private double GetStDev(Queue<int> ave20Queue)
        {
            // sqrt((x日の値 - N日ぶんの平均値)の2乗の合計 / N)
            var ave = ave20Queue.Average(elem => elem);
            var sum = ave20Queue.Sum(elem => Math.Pow((elem - ave), 2));

            double stdev = Math.Sqrt(sum / ave20Queue.Count);
            //double stdev = Math.Sqrt(sum) / ave20Queue.Count;

            return stdev;
        }

        /// <summary>
        /// 20日ぶんの単純移動平均を求める
        /// 20日なければその分の単純平均
        /// </summary>
        /// <param name="val"></param>
        /// <param name="ave20Queue"></param>
        /// <returns></returns>
        private int GetAve20(int val, Queue<int> ave20Queue)
        {
            int ave = 0;
            if (ave20Queue.Count == Ave20Max)
            {
                ave20Queue.Dequeue();
            }

            ave20Queue.Enqueue(val);
            var tmp = ave20Queue.Aggregate((sum, elem) => sum + elem);
            ave = tmp / ave20Queue.Count;

            return ave;
        }

        /// <summary>
        /// EMA を変更して wholeData を計算しなおす
        /// </summary>
        private void CalcWholeDataByEMA(int ema1, int ema2)
        {
            int prevMacdSig = int.MinValue;
            int priceMin = int.MaxValue;
            int priceMax = int.MinValue;

            double alpha8 = GetAlpha(8);
            double alpha9 = GetAlpha(9);
            double alpha12 = GetAlpha(ema1);
            double alpha26 = GetAlpha(ema2);

            int prevEma12 = (int)this.wholeData.Rows[0][GetEnumName(keyWd.END)];
            int prevEma26 = prevEma12;
            int prevEma8 = prevEma12;
            bool flag = true;
            List<DataRow> dataRowList = new List<DataRow>();
            foreach (DataRow r in this.wholeData.Rows)
            {
                // 元データコピー
                DataRow newRow = this.wholeData.NewRow();
                newRow[GetEnumName(keyWd.DATE)] = r[GetEnumName(keyWd.DATE)];
                newRow[GetEnumName(keyWd.START)] = r[GetEnumName(keyWd.START)];
                newRow[GetEnumName(keyWd.END)] = r[GetEnumName(keyWd.END)];
                newRow[GetEnumName(keyWd.MIN)] = r[GetEnumName(keyWd.MIN)];
                newRow[GetEnumName(keyWd.MAX)] = r[GetEnumName(keyWd.MAX)];
                newRow[GetEnumName(keyWd.VOLUME)] = r[GetEnumName(keyWd.VOLUME)];
                newRow[GetEnumName(keyWd.SMA20)] = r[GetEnumName(keyWd.SMA20)];
                newRow[GetEnumName(keyWd.BB_MINUS1)] = r[GetEnumName(keyWd.BB_MINUS1)];
                newRow[GetEnumName(keyWd.BB_MINUS2)] = r[GetEnumName(keyWd.BB_MINUS2)];
                newRow[GetEnumName(keyWd.BB_PLUS1)] = r[GetEnumName(keyWd.BB_PLUS1)];
                newRow[GetEnumName(keyWd.BB_PLUS2)] = r[GetEnumName(keyWd.BB_PLUS2)];

                int tmpend = (int)r[GetEnumName(keyWd.END)];
                int todayEma12 = GetEMA(prevEma12, tmpend, alpha12);
                prevEma12 = todayEma12;

                int todayEma26 = GetEMA(prevEma26, tmpend, alpha26);
                prevEma26 = todayEma26;

                int todayEma8 = GetEMA(prevEma8, tmpend, alpha8);
                prevEma8 = todayEma8;

                int todayMacd = todayEma12 - todayEma26;
                if (prevMacdSig == int.MinValue)
                {
                    prevMacdSig = todayMacd;
                }
                int todayMacdSig = GetEMA(prevMacdSig, todayMacd, alpha9);

                // シグナル算出
                string signal = string.Empty;
                int signal_point = 0;
                if (r == this.wholeData.Rows[0])
                {
                    // 最初のデータの場合にはフラグ初期化
                    flag = todayMacd > todayMacdSig;
                }

                if (flag == true && todayMacd < todayMacdSig)
                {
                    // MACDがシグナルを上から下に抜いた → 売りシグナル
                    if (todayMacd > 0)
                    {
                        signal = "売り";
                        signal_point = todayMacd;
                    }
                    flag = false;
                }
                else if (flag == false && todayMacd > todayMacdSig)
                {
                    // MACDがシグナルを下から上に抜いた → 買いシグナル
                    if (todayMacd < 0)
                    {
                        signal = "買い";
                        signal_point = todayMacd;
                    }
                    flag = true;
                }

                prevMacdSig = todayMacdSig;

                newRow[GetEnumName(keyWd.EMA12)] = todayEma12;
                newRow[GetEnumName(keyWd.EMA26)] = todayEma26;
                newRow[GetEnumName(keyWd.EMA8)] = todayEma8;
                newRow[GetEnumName(keyWd.MACD)] = todayMacd;
                newRow[GetEnumName(keyWd.MACD_SIG)] = todayMacdSig;
                newRow[GetEnumName(keyWd.SIGNAL)] = signal;

                if (signal_point != 0)
                {
                    if (flag == true)
                    {
                        newRow[GetEnumName(keyWd.SIGNAL_POINT_BUY_2)] = signal_point;
                    }
                    else
                    {
                        newRow[GetEnumName(keyWd.SIGNAL_POINT_SELL_2)] = signal_point;
                    }
                }
                priceMin = Math.Min(priceMin, (int)newRow[GetEnumName(keyWd.MIN)]);
                priceMax = Math.Max(priceMax, (int)newRow[GetEnumName(keyWd.MAX)]);

                dataRowList.Add(newRow);
            }

            this.wholeData.Rows.Clear();
            foreach (DataRow r in dataRowList)
            {
                this.wholeData.Rows.Add(r);
            }
        }

        /// <summary>
        /// 日付文字列 → DateTime
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private DateTime ConvertStringToDateTime(string str)
        {
            string date = str.Insert(6, "/");
            date = date.Insert(4, "/");
            return DateTime.Parse(date);
        }

        private void SetChart1()
        {
            this.chart1.Series.Clear();

            // 株価
            string price = GetEnumName(keyWd.PRICE);
            this.chart1.Series.Add(price);
            this.chart1.Series[price].ChartType = SeriesChartType.Candlestick;
            this.chart1.Series[price].XValueType = ChartValueType.Date;
            this.chart1.Series[price].XValueMember = "date";
            this.chart1.Series[price].YValueType = ChartValueType.Int32;
            this.chart1.Series[price].YValueMembers = string.Format("{0},{1},{2},{3}", "max", "min", "start", "end");
            this.chart1.Series[price].SetCustomProperty("PriceUpColor", "Red");
            this.chart1.Series[price].SetCustomProperty("PriceDownColor", "Blue");
            this.chart1.Series[price].IsXValueIndexed = true;
            this.chart1.Series[price].ToolTip = @"#VALX{d}
Max:#VALY1
min:#VALY2
s:#VALY3
e:#VALY4";
            //// EMA12(default=12)
            //SetTypeLineSeries(this.chart1, GetEnumName(keyWd.EMA1), "date");

            //// EMA26(default=26)
            //SetTypeLineSeries(this.chart1, GetEnumName(keyWd.EMA2), "date");
            SetTypeLineSeries(this.chart1, GetEnumName(keyWd.EMA8), "date", "ChartArea1");

            // 20日移動平均とボリンジャーバンド
            if (this.checkBoxBollingerband.Checked == true)
            {
                SetTypeLineSeries(this.chart1, GetEnumName(keyWd.SMA20), "date", "ChartArea1");
                SetTypeLineSeries(this.chart1, GetEnumName(keyWd.BB_MINUS1), "date", "ChartArea1");
                SetTypeLineSeries(this.chart1, GetEnumName(keyWd.BB_MINUS2), "date", "ChartArea1");
                SetTypeLineSeries(this.chart1, GetEnumName(keyWd.BB_PLUS1), "date", "ChartArea1");
                SetTypeLineSeries(this.chart1, GetEnumName(keyWd.BB_PLUS2), "date", "ChartArea1");
            }

            // シグナル表示のポイントグラフ設定
            SetTypePointSeries(this.chart1, GetEnumName(keyWd.SIGNAL_POINT_BUY_1_WIN), "date", MarkerStyle.Circle, Color.Green, "ChartArea1");
            SetTypePointSeries(this.chart1, GetEnumName(keyWd.SIGNAL_POINT_BUY_1_LOSE), "date", MarkerStyle.Circle, Color.Black, "ChartArea1");
            SetTypePointSeries(this.chart1, GetEnumName(keyWd.SIGNAL_POINT_SELL_1_WIN), "date", MarkerStyle.Triangle, Color.Green, "ChartArea1");
            SetTypePointSeries(this.chart1, GetEnumName(keyWd.SIGNAL_POINT_SELL_1_LOSE), "date", MarkerStyle.Triangle, Color.Black, "ChartArea1");
            this.chart1.Series[GetEnumName(keyWd.SIGNAL_POINT_BUY_1_WIN)].ToolTip = @"#VALX{d} #VALY";
            this.chart1.Series[GetEnumName(keyWd.SIGNAL_POINT_BUY_1_LOSE)].ToolTip = @"#VALX{d} #VALY";
            this.chart1.Series[GetEnumName(keyWd.SIGNAL_POINT_SELL_1_WIN)].ToolTip = @"#VALX{d} #VALY";
            this.chart1.Series[GetEnumName(keyWd.SIGNAL_POINT_SELL_1_LOSE)].ToolTip = @"#VALX{d} #VALY";


            //// 表示領域調整
            this.chart1.ChartAreas[0].InnerPlotPosition.Auto = false;
            this.chart1.ChartAreas[0].InnerPlotPosition.Width = 90;
            this.chart1.ChartAreas[0].InnerPlotPosition.Height = 90;
            this.chart1.ChartAreas[0].InnerPlotPosition.X = 10;
            this.chart1.ChartAreas[0].InnerPlotPosition.Y = 5;


            // MACD表示のグラフ設定
            SetTypeLineSeries(this.chart1, GetEnumName(keyWd.MACD), "date", "ChartArea2");
            SetTypeLineSeries(this.chart1, GetEnumName(keyWd.MACD_SIG), "date", "ChartArea2");

            // シグナル表示のポイントグラフ設定
            SetTypePointSeries(this.chart1, GetEnumName(keyWd.SIGNAL_POINT_BUY_2), "date", MarkerStyle.Circle, Color.Red, "ChartArea2");
            SetTypePointSeries(this.chart1, GetEnumName(keyWd.SIGNAL_POINT_SELL_2), "date", MarkerStyle.Cross, Color.Blue, "ChartArea2");

            // 表示領域調整
            this.chart1.ChartAreas[1].InnerPlotPosition.Auto = false;
            this.chart1.ChartAreas[1].InnerPlotPosition.Width = 90;
            this.chart1.ChartAreas[1].InnerPlotPosition.Height = 90;
            this.chart1.ChartAreas[1].InnerPlotPosition.X = 10;
            this.chart1.ChartAreas[1].InnerPlotPosition.Y = 5;

        }

        /// <summary>
        /// SeriesChartType.Line 型のグラフ設定
        /// </summary>
        /// <param name="chart"></param>
        /// <param name="seriesKey"></param>
        /// <param name="xvalMember"></param>
        /// <param name="yvalMember"></param>
        void SetTypeLineSeries(Chart chart, string seriesKey, string xvalMember, string chartArea)
        {
            chart.Series.Add(seriesKey);
            chart.Series[seriesKey].ChartType = SeriesChartType.Line;
            chart.Series[seriesKey].XValueType = ChartValueType.Date;
            chart.Series[seriesKey].XValueMember = xvalMember;
            chart.Series[seriesKey].YValueType = ChartValueType.Int32;
            chart.Series[seriesKey].YValueMembers = seriesKey;
            chart.Series[seriesKey].IsXValueIndexed = true;
            chart.Series[seriesKey].ChartArea = chartArea;
        }

        /// <summary>
        /// SeriesChartType.Point 型のグラフ設定
        /// </summary>
        /// <param name="chart"></param>
        /// <param name="seriesKey"></param>
        /// <param name="xvalMember"></param>
        /// <param name="mark"></param>
        private void SetTypePointSeries(Chart chart, string seriesKey, string xvalMember, MarkerStyle mark, Color color, string chartArea)
        {
            chart.Series.Add(seriesKey);
            chart.Series[seriesKey].ChartType = SeriesChartType.Point;
            chart.Series[seriesKey].XValueType = ChartValueType.Date;
            chart.Series[seriesKey].XValueMember = xvalMember;
            chart.Series[seriesKey].YValueType = ChartValueType.Int32;
            chart.Series[seriesKey].YValueMembers = seriesKey;
            chart.Series[seriesKey].IsXValueIndexed = true;
            chart.Series[seriesKey].MarkerStyle = mark;
            chart.Series[seriesKey].MarkerSize = 10;
            chart.Series[seriesKey].MarkerColor = color;
            chart.Series[seriesKey].ChartArea = chartArea;
        }

        /// <summary>
        /// 指数平滑移動平均
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        private int GetEMA(int prevVal, int todayVal, double alpha)
        {
            // [移動平均の日数をn日とした場合]
            // 指数平滑移動平均(EMA)＝前日のEMA+α(当日終値－前日のEMA)
            // ※α=2÷(n+1)
            return (int)(prevVal + alpha * (todayVal - prevVal));
        }

        /// <summary>
        /// EMAを求めるためのα値
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        private double GetAlpha(int n)
        {
            return (double)(2 / (double)(n + 1));
        }

        /// <summary>
        /// 日時From変更
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dateTimePicker1_ValueChanged(object sender, EventArgs e)
        {
            DateTimePicker dtp = sender as DateTimePicker;
            if (dtp.Value > this.dateTimePicker2.Value)
            {
                // 日時FromがToより大きくならないように
                dtp.Value = this.dateTimePicker2.Value.AddDays(-1);
            }
        }

        /// <summary>
        /// 日時To変更
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dateTimePicker2_ValueChanged(object sender, EventArgs e)
        {
            DateTimePicker dtp = sender as DateTimePicker;
            if (dtp.Value < this.dateTimePicker1.Value)
            {
                // 日時ToがFromより小さくならないように
                dtp.Value = this.dateTimePicker1.Value.AddDays(1);
            }
        }

        /// <summary>
        /// DataGridView1 に行番号表示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dataGridView1_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            dataGridView_RowPostPaint(sender, e);
        }

        /// <summary>
        /// DataGridView2 に行番号表示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dataGridView2_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            dataGridView_RowPostPaint(sender, e);
        }

        /// <summary>
        /// DataGridView に行番号表示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void dataGridView_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            if (dgv.RowHeadersVisible)
            {
                //行番号を描画する範囲を決定する
                Rectangle rect = new Rectangle(
                    e.RowBounds.Left, e.RowBounds.Top,
                    dgv.RowHeadersWidth, e.RowBounds.Height);
                rect.Inflate(-2, -2);
                //行番号を描画する
                TextRenderer.DrawText(e.Graphics,
                    (e.RowIndex + 1).ToString(),
                    e.InheritedRowStyle.Font,
                    rect,
                    e.InheritedRowStyle.ForeColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(this.textBoxInput.Text) == true)
                {
                    // (あれば)データロード
                    LoadData();
                }

                // グラフのセット
                SetChart1();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        /// <summary>
        /// dataGridView の行選択
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dataGridView1_SelectionChanged(object sender, EventArgs e)
        {
            if (this.isDraw == false)
            {
                return;
            }

            foreach (DataGridViewRow r in this.dataGridView1.SelectedRows)
            {
                DateTime cell = (DateTime)(r.Cells[GetEnumName(keyWd.DATE)].Value);
                this.dateTimePicker3.Value = cell;
            }
        }

        /// <summary>
        /// Target日の値段で買い/売りし、先に指定の利益、または損失額になるか算出する
        /// </summary>
        /// <param name="orgRow"></param>
        /// <param name="dataTbl"></param>
        private void CalcTgtDayProfit(DataRow orgRow, DataTable dataTbl)
        {
            // 指定日以降のDataTbaleを取得 
            DateTime dttgtDate = (DateTime)(orgRow[GetEnumName(keyWd.DATE)]);
            string tgtDateStr = dttgtDate.ToString("yyyy/MM/dd 00:00:00");
            DataRow[] rowAry = this.wholeData.Select(string.Format("{0} >= '{1}'", GetEnumName(keyWd.DATE), tgtDateStr));
            if (rowAry.Length == 0)
            {
                MessageBox.Show("何か日付が変:{0}", tgtDateStr);
                return;
            }

            double rieki = (double)this.numericUpDownRieki.Value / 100.0;
            double sonsitu = (double)this.numericUpDownSon.Value / 100.0 * (-1.0);
            int startPrice = (int)rowAry[0][GetEnumName(keyWd.START)];
            int sonneki = 0;
            //bool isRikaku = false;
            bool buy = (string)rowAry[0][GetEnumName(keyWd.SIGNAL)] == "買い";
            keyWd winLose = keyWd.SIGNAL_POINT_BUY_1_WIN;
            DateTime dtEnd = DateTime.MinValue;
            //int prevEma8 = int.MaxValue;
            for (int i = 1; i < rowAry.Length; i++)
            {
                int start = (int)rowAry[i][GetEnumName(keyWd.START)];
                int end = (int)rowAry[i][GetEnumName(keyWd.END)];
                int min = (int)rowAry[i][GetEnumName(keyWd.MIN)];
                int max = (int)rowAry[i][GetEnumName(keyWd.MAX)];
                double wariai = ((double)(end - startPrice)) / startPrice;
                sonneki = end - startPrice;
                if (buy == false)
                {
                    wariai *= -1.0;
                    sonneki *= -1;
                }

#if true
                if (wariai >= rieki || wariai <= sonsitu)
                {
                    if (wariai >= rieki)
                    {
                        winLose = buy == true ? keyWd.SIGNAL_POINT_BUY_1_WIN : keyWd.SIGNAL_POINT_SELL_1_WIN;
                    }
                    else
                    {
                        winLose = buy == true ? keyWd.SIGNAL_POINT_BUY_1_LOSE : keyWd.SIGNAL_POINT_SELL_1_LOSE;
                    }
                    dtEnd = (DateTime)rowAry[i][GetEnumName(keyWd.DATE)];
                    int span = ((TimeSpan)(dtEnd - dttgtDate)).Days;
                    orgRow["span"] = span;
                    if (span > 100)
                    {
                        winLose = buy == true ? keyWd.SIGNAL_POINT_BUY_1_LOSE : keyWd.SIGNAL_POINT_SELL_1_LOSE;
                        if (sonneki > 0)
                        {
                            sonneki *= -1;
                        }
                    }
                    break;
                }
#else
                    int todayEma8 = (int)rowAry[i][GetEnumName(keyWd.EMA8)];
                if (wariai >= rieki || isRikaku == true)
                {
                    isRikaku = true;
                    // 5%を超えたら、指数平滑移動平均線(8日)が下向きになるまで利確しない
                    if (buy == true)
                    {
                        if (wariai < rieki || todayEma8 - prevEma8 < 0)
                        {
                            winLose = keyWd.SIGNAL_POINT_BUY_1_WIN;
                            dtEnd = (DateTime)rowAry[i][GetEnumName(keyWd.DATE)];
                            break;
                        }
                    }
                    else
                    {
                        if (wariai < rieki || todayEma8 - prevEma8 > 0)
                        {
                            winLose = keyWd.SIGNAL_POINT_SELL_1_WIN;
                            dtEnd = (DateTime)rowAry[i][GetEnumName(keyWd.DATE)];
                            break;
                        }
                    }
                }
                if (wariai <= sonsitu)
                {
                    winLose = buy == true ? keyWd.SIGNAL_POINT_BUY_1_LOSE : keyWd.SIGNAL_POINT_SELL_1_LOSE;
                    dtEnd = (DateTime)rowAry[i][GetEnumName(keyWd.DATE)];
                    break;
                }
                prevEma8 = todayEma8;
#endif
            }
            this.periodData.Rows.Find(tgtDateStr)[GetEnumName(winLose)] = startPrice;

            orgRow["利確日"] = dtEnd;
            orgRow["損益"] = sonneki;
        }

        private void button1_Click(object sender, EventArgs e)
        {
        }

        private void checkBoxBollingerband_CheckedChanged(object sender, EventArgs e)
        {
            SetChart1();
        }

        private void dataGridView3_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            dataGridView_RowPostPaint(sender, e);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.Save();
        }

        private void buttonInput_Click(object sender, EventArgs e)
        {
            OpenFileDialog fd = new OpenFileDialog();
            fd.FileName = this.textBoxInput.Text;
            if (fd.ShowDialog(this) == DialogResult.Cancel)
            {
                return;
            }
            this.textBoxInput.Text = fd.FileName;
        }

        private void buttonLoad_Click(object sender, EventArgs e)
        {
            bool st = LoadData();
        }

        private bool LoadData()
        {
            if (this.textBoxInput.Text == string.Empty || File.Exists(this.textBoxInput.Text) == false)
            {
                MessageBox.Show("不正なファイル名");
                return false;
            }
            
            // データ取得
            string[] allTxtAry = File.ReadAllText(this.textBoxInput.Text, System.Text.Encoding.GetEncoding("shift-jis")).Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            List<Data> dataList = new List<Data>();
            foreach (string line in allTxtAry)
            {
                if (String.IsNullOrEmpty(line.Trim()) == true)
                {
                    continue;
                }
                string[] str = line.Split(new string[] { ",", ";" }, StringSplitOptions.None);

                Data data = new Data { date = str[0], start = int.Parse(str[1]), max = int.Parse(str[2]), min = int.Parse(str[3]), end = int.Parse(str[4]), volume = int.Parse(str[5]) };
                dataList.Add(data);
            }

            this.dateTimePicker1.Enabled = this.dateTimePicker2.Enabled = this.dateTimePicker3.Enabled = true;
            this.dateTimePicker1.Value = ConvertStringToDateTime(dataList[0].date);
            this.dateTimePicker2.Value = ConvertStringToDateTime(dataList[dataList.Count - 1].date);

            this.buttonRun.Enabled = true;

            // 入力値を元にした全体 DataTable 生成
            CreateWholeData(dataList);

            return true;
        }
    }
}
