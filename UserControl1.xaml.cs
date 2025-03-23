using System;
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
        private void LoadFitCSV(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();

            // Настраиваем фильтр для выбора только CSV файлов
            openFileDialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
            openFileDialog.FilterIndex = 1; // По умолчанию выбираем CSV

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                
            }
        }
    }
}
