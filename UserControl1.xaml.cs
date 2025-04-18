﻿using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using Autodesk.Revit.DB;

namespace CalcFittingsPlugin
{
    /// <summary>
    /// Логика взаимодействия для UserControl1.xaml
    /// </summary>
    public partial class UserControl1 : Window
    {
        static private int NumOfSol;
        static private DataTable DiamStep;
        static private DataTable DiamCost;
        static private DataTable Length;
        static private string FlrName;
        static private DataTable FitDataTable;


        public UserControl1()
        {
            InitializeComponent();

            DiamStep = new DataTable();
            DiamCost = new DataTable();
            Length = new DataTable();
            FitDataTable = new DataTable();

            InitializeDataTables();

            //Валидиируем JSON и загружаем данные
            string msg = DataFile.ValidateJSONFile();
            ConsoleLog.AppendText(Tools.CreateLogMessage(msg));
            DataFile.LoadAllData(DiamStep, DiamCost, Length);

            NumOfSol = Properties.Settings.Default.MaxSol;

            if (NumOfSol == 0)
                MaxSolTextBox.Text = "";
            else
                MaxSolTextBox.Text = NumOfSol.ToString();



            FlrName = Properties.Settings.Default.FlrName;
            FlrTextBox.Text = FlrName;

            ArmTextBox.Text = Properties.Settings.Default.MainFit;
        }

        private void InitializeDataTables()
        {
            //Заполняем Диаметр – Шаг
            DiamStep.Columns.Add("Diam", typeof(int));
            DiamStep.Columns.Add("Step", typeof(int));

            //Заполняем Диаметр – Цена
            DiamCost.Columns.Add("Diam", typeof(int));
            DiamCost.Columns.Add("Cost", typeof(int));

            //Заполняем Длина
            Length.Columns.Add("Length", typeof(int));

            //Заполняем таблицу арматуры
            FitDataTable.Columns.Add(Tools.HeadersTemplate[0], typeof(string));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[1], typeof(int));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[2], typeof(double));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[3], typeof(double));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[4], typeof(double));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[5], typeof(double));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[6], typeof(double));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[7], typeof(double));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[8], typeof(double));
            FitDataTable.Columns.Add(Tools.HeadersTemplate[9], typeof(double));
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        //Кнопка открытия окна редактора данных арматуры
        private void OpenFitDataEditor(object sender, RoutedEventArgs e)
        {
            UserControl2 fitDataWindow = new UserControl2();
            fitDataWindow.Owner = this;
            fitDataWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            fitDataWindow.ShowDialog();

            //Если хоть единожды вызывалось сохранение данных – считаем, что данные были обновлены
            if (fitDataWindow.getIsDataChanged())
            {
                DataFile.LoadAllData(DiamStep, DiamCost, Length);
                ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.SucUpdateJSON));
            }
        }

        private void TextBox_MaxSol_TextChanged(object sender, TextChangedEventArgs e)
        {
            string newText = "";
            //Очищаем текст от не цифр

            for(int i = 0; i< MaxSolTextBox.Text.Length; i++)
            {
                string symb = "" + MaxSolTextBox.Text[i];
                if (Tools.IsInt(symb))
                {
                    newText += symb;
                }
            }

            MaxSolTextBox.Text = newText;

            if (MaxSolTextBox.Text == "")
            {
                NumOfSol = 0;
            }
            else
                NumOfSol = int.Parse(MaxSolTextBox.Text);
        }

        //Сохраняем настройку максимального числа получаемых решений
        protected override void OnClosed(EventArgs e)
        {
            Properties.Settings.Default.MaxSol = NumOfSol;
            Properties.Settings.Default.FlrName = FlrName;
            Properties.Settings.Default.MainFit = ArmTextBox.Text;
            Properties.Settings.Default.Save();
            base.OnClosed(e);
        }

        //Запрещаем ввод нечисловых значений и нуля как первого символа
        private void MaxSolTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if ((!Tools.IsInt(e.Text)) || ((e.Text == "0") && (MaxSolTextBox.Text.Length == 0)))
            {
                e.Handled = true;
            }
        }

        //Событие – загрузка основного армирования
        private async void LoadFitCSV(object sender, RoutedEventArgs e)
        {
            
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
            openFileDialog.FilterIndex = 1;

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                ProgressWindow progressWindow = null;

                try
                {
                    ApplyBtn.IsEnabled = false;
                    CancelBtn.IsEnabled = false;

                    //Сбрасываем модель к исходной, удаляем все текущие решения
                    //to-do

                    this.IsEnabled = false;
                    // Создаем и настраиваем окно в UI-потоке
                    progressWindow = new ProgressWindow
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Topmost = true
                    };

                    // Показываем окно
                    progressWindow.Show();

                    // Добавляем сообщение в лог
                    ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.ParseStart));

                    await ParseCsvWithProgress(openFileDialog.FileName, progressWindow);
                    progressWindow.UpdateProgress(100, "Завершено");
                    await Task.Delay(1000); // Даем время увидеть 100%

                    ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.ParceEnd));
                    CalcFittingBtn.IsEnabled = true;
                }
                catch
                {
                    ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.ParceErr));
                    CalcFittingBtn.IsEnabled = false;
                }
                finally
                {
                    progressWindow.SafeClose();
                    this.Focus();
                    this.Activate();
                    this.IsEnabled = true;
                }
            }
        }


        private async Task ParseCsvWithProgress(string filePath, ProgressWindow progressWindow)
        {
            long totalLines = CountFileLines(filePath);
            long processedLines = 0;

            try
            {
                FitDataTable.Clear();
                var encoding = DetectFileEncoding(filePath);
                using (var parser = new TextFieldParser(filePath, encoding))
                {
                    parser.TextFieldType = FieldType.Delimited;
                    parser.SetDelimiters(";");
                    parser.HasFieldsEnclosedInQuotes = false;

                    // Читаем заголовки
                    string[] headers = parser.ReadFields();

                    // Валидируем заголовки файла
                    if (headers.Length != Tools.HeadersCount)
                    {
                        MessageBox.Show(
                            $"Ошибка при загрузке файла: несоответствие числа заголовков (" +
                            headers.Length.ToString() + ") с ожидаемым (" + Tools.HeadersCount.ToString() + ").", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning
                            );
                        throw new Exception();
                    }

                    // Валидируем имена заголовков
                    for (int i = 0; i < Tools.HeadersCount; i++)
                    {
                        if (!string.Equals(headers[i], Tools.HeadersTemplate[i], StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show(
                                $"Ошибка при загрузке файла: ожидался заголовок (" +
                                Tools.HeadersTemplate[i] + "), но вместо него найден (" + headers[i] + ").", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Warning
                                );
                            throw new Exception(); 
                        }
                    }

                    // Основной цикл обработки данных
                    while (!parser.EndOfData)
                    {
                        DataRow row = FitDataTable.NewRow();

                        processedLines++;

                        string[] fields = parser.ReadFields();

                        if (!int.TryParse(fields[1], out _) || 
                            !double.TryParse(fields[2], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(fields[3], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(fields[4], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(fields[5], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(fields[6], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(fields[7], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(fields[8], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                            !double.TryParse(fields[9], NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                            )
                        {
                            MessageBox.Show($"Ошибка типа данных в строке " + processedLines, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

                            throw new Exception();
                        }

                        row[Tools.HeadersTemplate[0]] = fields[0];
                        row[Tools.HeadersTemplate[1]] = int.Parse(fields[1]);
                        row[Tools.HeadersTemplate[2]] = double.Parse(fields[2], CultureInfo.InvariantCulture);
                        row[Tools.HeadersTemplate[3]] = double.Parse(fields[3], CultureInfo.InvariantCulture);
                        row[Tools.HeadersTemplate[4]] = double.Parse(fields[4], CultureInfo.InvariantCulture);
                        row[Tools.HeadersTemplate[5]] = double.Parse(fields[5], CultureInfo.InvariantCulture);
                        row[Tools.HeadersTemplate[6]] = double.Parse(fields[6], CultureInfo.InvariantCulture);
                        row[Tools.HeadersTemplate[7]] = double.Parse(fields[7], CultureInfo.InvariantCulture);
                        row[Tools.HeadersTemplate[8]] = double.Parse(fields[8], CultureInfo.InvariantCulture);
                        row[Tools.HeadersTemplate[9]] = double.Parse(fields[9], CultureInfo.InvariantCulture);

                        FitDataTable.Rows.Add(row);

                        // Обновляем прогресс
                        int percent = (int)((double)processedLines / totalLines * 100);
                        progressWindow.UpdateProgress(percent, $"{percent}% ({processedLines}/{totalLines} строк)");
                        if (processedLines % 100 == 0) await Task.Delay(1);
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        public static Encoding DetectFileEncoding(string filePath)
        {
            using (var reader = new StreamReader(filePath, Encoding.Default, true))
            {
                reader.Peek(); // Необходимо для определения кодировки
                return reader.CurrentEncoding;
            }
        }

        private long CountFileLines(string filePath)
        {
            long count = 0;
            using (var reader = new StreamReader(filePath))
            {
                while (reader.ReadLine() != null)
                {
                    count++;
                }
            }
            return count;
        }

        private string ValidateData()
        {
            if(DiamStep.Rows.Count == 0)
            {
                return "Не найдено комбинаций Диаметр-Шаг, проверьте Данные по арматуре.";
            }
            if (DiamCost.Rows.Count == 0)
            {
                return "Не найдено комбинаций Диаметр-Цена, проверьте Данные по арматуре.";
            }
            if (Length.Rows.Count == 0)
            {
                return "Не найдено комбинаций Длины, проверьте Данные по арматуре.";
            }
            return "";
        }

        private void FlrTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FlrName = FlrTextBox.Text;
        }

        private async void CalcFittingBtn_Click(object sender, RoutedEventArgs e)
        {
            this.IsEnabled = false;
            ProgressWindow progressWindow = new ProgressWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Topmost = true,
                Title = "Расчет зон дополнительного армирования"
            };

            ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.CalcStart));

            try
            {
                progressWindow.Show();

                List<Floor> floors = null;

                if(!Command.ValidateLevel(FlrName, Command.uiDoc, out floors))
                {
                    throw new Exception();
                }

                double MainFit = 0;

                if(!double.TryParse(ArmTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out MainFit))
                {
                    if (!double.TryParse(ArmTextBox.Text, out MainFit))
                    {
                        MessageBox.Show("Значение основного армирования должно быть дробным числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        throw new Exception();
                    }
                }

                if(NumOfSol == 0)
                {
                    MessageBox.Show("Не задано максимально допустимое число получаемых решений.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    throw new Exception();
                }

                //Валидируем данные арматуры, чтобы их можно было использовать
                //в зонах дополнительного армирования
                string msg = ValidateData();
                if(msg != "")
                {
                    MessageBox.Show(msg, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    throw new Exception();
                }

                progressWindow.UpdateProgress(33, "Определение узлов, превыщающих основное армирование.");
                await Task.Delay(1000);

                //Удаляем wall узлы и такие, которые покрываются основным армированием
                DataTable needFit = FitDataTable.Copy();
                needFit.Clear();

                for (int i = 0; i < FitDataTable.Rows.Count; i++)
                {
                    DataRow row = FitDataTable.Rows[i];

                    bool isWall = row[Tools.HeadersTemplate[0]].ToString() == "Wall";
                    bool isThin1 = Convert.ToDouble(row[Tools.HeadersTemplate[6]]) < MainFit;
                    bool isThin2 = Convert.ToDouble(row[Tools.HeadersTemplate[7]]) < MainFit;
                    bool isThin3 = Convert.ToDouble(row[Tools.HeadersTemplate[8]]) < MainFit;
                    bool isThin4 = Convert.ToDouble(row[Tools.HeadersTemplate[9]]) < MainFit;

                    bool shouldSkip = isWall || (isThin1 && isThin2 && isThin3 && isThin4);

                    if (!shouldSkip)
                    {
                        DataRow newRow = needFit.NewRow();
                        newRow.ItemArray = row.ItemArray; // Копируем все значения
                        needFit.Rows.Add(newRow);
                    }
                }

                var optimizer = new ReinforcementOptimizer
                {
                    Openings = ReinforcementOptimizer.GetOpeningsFromRevit(floors),
                    BasicReinforcement = new[] { MainFit, MainFit, MainFit, MainFit },
                    StandardLengths = Length.AsEnumerable()
                    .Select(r => Convert.ToDouble(r["Length"]))
                    .ToList(),
                    AvailableRebars = DiamStep.AsEnumerable()
                    .GroupBy(r => Convert.ToInt32(r["Diam"]))
                    .Select(g => new RebarConfig
                    {
                        Diameter = g.Key,
                        AvailableSpacings = g.Select(r => Convert.ToDouble(r["Step"])).ToList(),
                        PricePerMeter = DiamCost.AsEnumerable()
                        .FirstOrDefault(r => Convert.ToInt32(r["Diam"]) == g.Key)?["Cost"] != null
                           ? Convert.ToDouble(DiamCost.AsEnumerable()
                            .First(r => Convert.ToInt32(r["Diam"]) == g.Key)["Cost"]) : 0,
                    })
                    .Where(r => r.PricePerMeter > 0)
                    .ToList()
                };

                List<DataTable> floorPoints = new List<DataTable>();
                var slabsNodes = new List<List<Node>>();
                //Формируем для каждой плиты перекрытия ее узлы
                if (floors != null)
                {
                    for(int i = 0; i < floors.Count; i++)
                    {
                        DataTable floorTable = needFit.Copy();
                        floorTable.Clear();
                        Command.GetNodeTable(floors[i], needFit, out floorTable);

                        slabsNodes.Add(floorTable.AsEnumerable()
                            .Select(r => new Node
                            {
                                Type = r[Tools.HeadersTemplate[0]].ToString(),
                                Number = Convert.ToInt32(r[Tools.HeadersTemplate[1]]),
                                X = Convert.ToDouble(r[Tools.HeadersTemplate[2]]),
                                Y = Convert.ToDouble(r[Tools.HeadersTemplate[3]]),
                                ZCenter = Convert.ToDouble(r[Tools.HeadersTemplate[4]]),
                                ZMin = Convert.ToDouble(r[Tools.HeadersTemplate[5]]),
                                As1X = Convert.ToDouble(r[Tools.HeadersTemplate[6]]),
                                As2X = Convert.ToDouble(r[Tools.HeadersTemplate[7]]),
                                As3Y = Convert.ToDouble(r[Tools.HeadersTemplate[8]]),
                                As4Y = Convert.ToDouble(r[Tools.HeadersTemplate[9]]),
                                SlabId = i
                            })
                            .ToList());
                    }
                }

                if(!slabsNodes.Any(sn => sn.Count > 0))
                {
                    MessageBox.Show("Для плит перекрытия уровня '" + FlrName + "' не найдено узлов, превышающих основное армирование." , "Расчет", MessageBoxButton.OK, MessageBoxImage.Warning);
                    throw new Exception();
                }

                progressWindow.UpdateProgress(66, "Расчет вариантов зон дополнительного армирования.");
                await Task.Delay(1000);


                //Запускаем расчетный алгоритм
                var bestSolutions = optimizer.FindBestSolutions(slabsNodes, NumOfSol);

                progressWindow.UpdateProgress(100, "Завершено");
                await Task.Delay(1000); // Даем время увидеть 100%

                ConsoleLog.AppendText(Tools.CreateLogMessage("Получено решений " + bestSolutions.Count.ToString()));

                foreach (var solution in bestSolutions)
                {
                    ConsoleLog.AppendText(Tools.CreateLogMessage("Решение стоимостью "+ solution.TotalCost.ToString()));
                    ConsoleLog.AppendText(Tools.CreateLogMessage("Всего зон " + solution.Zones.Count.ToString()));
                    double length = 0;
                    foreach (var zone in solution.Zones)
                    {
                        length += zone.TotalLength;
                    }
                    ConsoleLog.AppendText(Tools.CreateLogMessage("Общая длина арматуры " + length));
                    //ConsoleLog.AppendText(Tools.CreateLogMessage("Длина арматуры " + solution.));
                    /*foreach (var zone in solution.Zones)
                    {
                        ConsoleLog.AppendText($"Зона: {zone.Boundary.Width}x{zone.Boundary.Height}, " +
                                      $"Арматура: Ø{zone.Rebar.Diameter}/{zone.Spacing}, " +
                                      $"Стоимость: {zone.TotalCost}");
                    }*/
                }

                if (bestSolutions.Count > 0 && Command.VisualizationEvent != null)
                {
                    Command.VisualizationHandler.Solution = bestSolutions[0]; // Первое решение
                    Command.VisualizationHandler.Floors = floors;

                    // Запускаем визуализацию
                    Command.VisualizationEvent.Raise();

                    // Запускаем визуализацию
                    Command.VisualizationEvent.Raise();

                    ConsoleLog.AppendText(Tools.CreateLogMessage("Визуализация запущена"));
                }


                ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.CalcSuc));

            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
                ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.CalcErr));
            }
            finally
            {
                await Task.Delay(1);
                progressWindow.SafeClose();
                this.Focus();
                this.Activate();
                this.IsEnabled = true;
            }
        }

        private void ArmTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string newText = "";
            //Очищаем текст от пробелов

            for (int i = 0; i < ArmTextBox.Text.Length; i++)
            {
                if(ArmTextBox.Text[i] != ' ')
                newText = newText + ArmTextBox.Text[i];
            }

            ArmTextBox.Text = newText;
        }

        private void ArmTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            for(int i = 0; i < e.Text.Length; i++)
            {
                if(Char.IsLetter(e.Text[i]))
                {
                    e.Handled = true;
                    break;
                }
            }
        }
    }
}
