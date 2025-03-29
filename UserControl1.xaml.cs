using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

namespace CalcFittingsPlugin
{
    /// <summary>
    /// Логика взаимодействия для UserControl1.xaml
    /// </summary>
    public partial class UserControl1 : Window
    {
        static private int NumOfSol;
        static private ArrayList DiamStep;
        static private ArrayList DiamCost;
        static private ArrayList Length;
        static private string FlrName;

        public UserControl1()
        {
            InitializeComponent();
            DiamStep = new ArrayList();
            DiamCost = new ArrayList();
            Length = new ArrayList();


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
                    // Создаем и настраиваем окно в UI-потоке
                    progressWindow = new ProgressWindow
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    // Показываем окно
                    progressWindow.Show();

                    // Добавляем сообщение в лог
                    ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.ParseStart));

                    await ParseCsvWithProgress(openFileDialog.FileName, progressWindow);
                    progressWindow.UpdateProgress(100, "Завершено");
                    await Task.Delay(1000); // Даем время увидеть 100%

                    ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.ParceEnd));
                }
                catch (Exception ex)
                {
                    ConsoleLog.AppendText(Tools.CreateLogMessage(Tools.ParceErr));
                }
                finally
                {
                    progressWindow.SafeClose();
                    this.Focus();
                    this.Activate();
                }
            }
        }


        private async Task ParseCsvWithProgress(string filePath, ProgressWindow progressWindow)
        {
            long totalLines = CountFileLines(filePath);
            long processedLines = 0;

            try
            {
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
                        string[] fields = parser.ReadFields();
                        processedLines++;

                        // Обновляем прогресс
                        int percent = (int)((double)processedLines / totalLines * 100);
                        progressWindow.UpdateProgress(percent, $"{percent}% ({processedLines}/{totalLines} строк)");
                        //await Task.Delay(1);
                    }
                }
            }
            catch (Exception ex)
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

        private void FlrTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FlrName = FlrTextBox.Text;
        }
    }
}
