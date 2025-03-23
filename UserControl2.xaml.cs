using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
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

namespace CalcFittingsPlugin
{
    public partial class UserControl2 : Window
    {
        private static DataTable DiamStep;
        private static DataTable DiamCost;
        private static DataTable Length;

        private object oldCellValue;

        //Выделенные строки в гридах
        private DataRowView _selectedDiamStepRow;
        private DataRowView _selectedDiamCostRow;
        private DataRowView _selectedLengthRow;

        //Флаг для отслеживания – менялись ли данные
        private static Boolean isDataChanged;

        public UserControl2()
        {
            DiamStep = new DataTable();
            DiamCost = new DataTable();
            Length = new DataTable();
            isDataChanged = false;
            InitializeComponent();

            //Следующий блок – подписываемся на различные однотипные события для DataGrid
            DiamStepView.BeginningEdit += DataGrid_BeginningEdit;
            DiamStepView.CellEditEnding += DataGrid_CellEditEnding;
            DiamStepView.SelectionChanged += DataGrid_SelectionChanged;

            DiamCostView.BeginningEdit += DataGrid_BeginningEdit;
            DiamCostView.CellEditEnding += DataGrid_CellEditEnding;
            DiamCostView.SelectionChanged += DataGrid_SelectionChanged;

            LengthView.BeginningEdit += DataGrid_BeginningEdit;
            LengthView.CellEditEnding += DataGrid_CellEditEnding;
            LengthView.SelectionChanged += DataGrid_SelectionChanged;

            DiamStepView.LoadingRow += DataGrid_LoadingRow;
            DiamCostView.LoadingRow += DataGrid_LoadingRow;
            LengthView.LoadingRow += DataGrid_LoadingRow;

            CreateDataProviders();
            DataFile.LoadAllData(DiamStep, DiamCost, Length);
            CreateFirstRow();
            RecalcNum(DiamStep);
            RecalcNum(DiamCost);
            RecalcNum(Length);
        }

        //Метод для создания таблиц и установления их в качестве провайдера данных 
        private void CreateDataProviders()
        {
            //Заполняем Диаметр – Шаг
            DiamStep.Columns.Add("Num", typeof(int));
            DiamStep.Columns.Add("Diam", typeof(int));
            DiamStep.Columns.Add("Step", typeof(int));
            DiamStepView.ItemsSource = DiamStep.DefaultView;

            //Заполняем Диаметр – Цена
            DiamCost.Columns.Add("Num", typeof(int));
            DiamCost.Columns.Add("Diam", typeof(int));
            DiamCost.Columns.Add("Cost", typeof(int));
            DiamCostView.ItemsSource = DiamCost.DefaultView;

            //Заполняем Длина
            Length.Columns.Add("Num", typeof(int));
            Length.Columns.Add("Length", typeof(int));
            LengthView.ItemsSource = Length.DefaultView;
        }

        private void CreateFirstRow()
        {
            //В таблицах всегда должна существовать хотя бы одна строка
            if (DiamStep.Rows.Count == 0)
                DiamStep.Rows.Add();

            if (DiamCost.Rows.Count == 0)
                DiamCost.Rows.Add();

            if (Length.Rows.Count == 0)
                Length.Rows.Add();

        }

        public Boolean getIsDataChanged()
        {
            return isDataChanged;
        }

        //функция – валидатор перед сохранением таблиц в файл
        private string ValidateSave()
        {
            Func<int, string> EmptyData = x => "    Строка " + x.ToString() + " – не все данные заданы.\n";
            Func<int, string> RepData = x => "    Строка " + x.ToString() + " – строка с такими данными уже существует.\n";
            Func<int, string> RepCost = x => "    Строка " + x.ToString() + " – цена для данного диаметра уже была задана раннее.\n";
            Func<int, string> LengthOutOfBounds = x => "    Строка " + x.ToString() + " – длина больше 11700 мм. или меньше 1000 мм.\n";
            Func<int, string> DiamNotCost = x => "    Для диаметра " + x.ToString() + " – не найдена строка в Диаметр – Цена.\n";

            string message = "";
            string diamCostMessage = "";
            string diamStepMessage = "";
            string LengthMessage = "";
            string invalideMessage = "";

            // Валидация таблицы Диаметр – Шаг
            for (int i = 0; i < DiamStep.Rows.Count; i++)
            {
                // Проверяем строку на пустоту (null или пустая строка)
                if (string.IsNullOrEmpty(DiamStep.Rows[i].ItemArray[1]?.ToString()) ||
                    string.IsNullOrEmpty(DiamStep.Rows[i].ItemArray[2]?.ToString()))
                {
                    diamStepMessage += EmptyData(i + 1);
                    continue;
                }

                // Проверка на дубликаты
                for (int j = i - 1; j >= 0; j--)
                {
                    if (DiamStep.Rows[i].ItemArray[1].ToString() == DiamStep.Rows[j].ItemArray[1].ToString() &&
                        DiamStep.Rows[i].ItemArray[2].ToString() == DiamStep.Rows[j].ItemArray[2].ToString())
                    {
                        diamStepMessage += RepData(i + 1);
                        break;
                    }
                }
            }

            // Валидация таблицы Диаметр – Цена
            for (int i = 0; i < DiamCost.Rows.Count; i++)
            {
                // Проверяем строку на пустоту (null или пустая строка)
                if (string.IsNullOrEmpty(DiamCost.Rows[i].ItemArray[1]?.ToString()) ||
                    string.IsNullOrEmpty(DiamCost.Rows[i].ItemArray[2]?.ToString()))
                {
                    diamCostMessage += EmptyData(i + 1);
                    continue;
                }

                // Проверка на дубликаты
                for (int j = i - 1; j >= 0; j--)
                {
                    if (DiamCost.Rows[i].ItemArray[1].ToString() == DiamCost.Rows[j].ItemArray[1].ToString())
                    {
                        diamCostMessage += RepCost(i + 1);
                        break;
                    }
                }
            }

            // Валидация таблицы Длина
            for (int i = 0; i < Length.Rows.Count; i++)
            {
                // Проверяем строку на пустоту (null или пустая строка)
                if (string.IsNullOrEmpty(Length.Rows[i].ItemArray[1]?.ToString()))
                {
                    LengthMessage += EmptyData(i + 1);
                    continue;
                }

                if (!int.TryParse(Length.Rows[i].ItemArray[1].ToString(), out int lengthValue))
                {
                    LengthMessage += EmptyData(i + 1);
                    continue;
                }

                // Проверяем нахождение значения в границах
                if (lengthValue > 11700 || lengthValue < 1000)
                {
                    LengthMessage += LengthOutOfBounds(i + 1);
                }

                // Проверка на дубликаты
                for (int j = i - 1; j >= 0; j--)
                {
                    if (Length.Rows[i].ItemArray[1].ToString() == Length.Rows[j].ItemArray[1].ToString())
                    {
                        LengthMessage += RepCost(i + 1);
                        break;
                    }
                }
            }

            //Проверяем данные на валидность
            Dictionary<int, bool> checkedDiameters = new Dictionary<int, bool>();

            for (int i = 0; i < DiamStep.Rows.Count; i++)
            {
                if (string.IsNullOrEmpty(DiamStep.Rows[i].ItemArray[1]?.ToString())
                    ||
                    !int.TryParse(DiamStep.Rows[i].ItemArray[1].ToString(), out int diamValue))
                {
                    continue;
                }

                if (checkedDiameters.ContainsKey(diamValue))
                {
                    continue;
                }

                bool isDiamFoundInCost = false; // Флаг, указывающий, найден ли диаметр в таблице DiamCost

                // Ищем диаметр в таблице DiamCost
                for (int j = 0; j < DiamCost.Rows.Count; j++)
                {
                    if (string.IsNullOrEmpty(DiamCost.Rows[j].ItemArray[1]?.ToString())
                        ||
                        !int.TryParse(DiamCost.Rows[j].ItemArray[1].ToString(), out int diamInCostValue))
                    {
                        continue;
                    }

                    if (diamInCostValue == diamValue)
                    {
                        isDiamFoundInCost = true;
                        break;
                    }
                }

                // Добавляем диаметр в словарь, чтобы отметить, что он был проверен
                checkedDiameters[diamValue] = isDiamFoundInCost;

                if (!isDiamFoundInCost)
                {
                    invalideMessage += DiamNotCost(diamValue);
                }
            }

            // Формируем итоговое сообщение
            diamStepMessage = (diamStepMessage.Length > 0) ? Tools.ForDiamStep + diamStepMessage : "";
            diamCostMessage = (diamCostMessage.Length > 0) ? Tools.ForDiamCost + diamCostMessage : "";
            LengthMessage = (LengthMessage.Length > 0) ? Tools.ForLength + LengthMessage : "";
            invalideMessage = (invalideMessage.Length > 0) ? Tools.InvalideData + invalideMessage : "";

            message = diamStepMessage + diamCostMessage + LengthMessage + invalideMessage;
            message = (message.Length > 0) ? Tools.ChangesNotSaved + message : "";

            return message;
        }

        //Данный метод общий для 3х гридов, нужен для отслеживания наличия выделенной строки
        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DataGrid dataGrid = sender as DataGrid;

            bool isRowSelected = dataGrid.SelectedItem != null;

            DeleteButton.IsEnabled = isRowSelected;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // При смене вкладки завершаем редактирование во всех DataGrid
            CommitAllDataGrids();

            // Обновляем состояние кнопки "Удалить"
            bool isRowSelected = false;
            switch (GridTab.SelectedIndex)
            {
                case 0: // Вкладка Диаметр – Шаг
                    isRowSelected = DiamStepView.SelectedItem != null;
                    break;
                case 1: // Вкладка Диаметр – Цена
                    isRowSelected = DiamCostView.SelectedItem != null;
                    break;
                case 2: // Вкладка Длина
                    isRowSelected = LengthView.SelectedItem != null;
                    break;
            }

            DeleteButton.IsEnabled = isRowSelected;
        }

        //Данный метод завершает редактирование во всех гридах, если завершить его не удалось – отменяет изменения
        private void CommitAllDataGrids()
        {
            if (!CommitDataGridEditing(DiamStepView))
            {
                DiamStepView.CancelEdit();
            }

            if (!CommitDataGridEditing(DiamCostView))
            {
                DiamCostView.CancelEdit();
            }

            if (!CommitDataGridEditing(LengthView))
            {
                LengthView.CancelEdit();
            }
        }

        //Данный метод отслеживает, удалось ли сохранить изменения в гриде
        private bool CommitDataGridEditing(DataGrid dataGrid)
        {
            try
            {
                bool commitSuccess = dataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                commitSuccess &= dataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                return commitSuccess;
            }
            catch
            {
                return false;
            }
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {

            // Выполняем сохранение
            CommitAllDataGrids();
            string validateMsg = ValidateSave();
            if (validateMsg == "")
            {
                try
                {
                    isDataChanged = true;
                    DataFile.UpdateAllData(DiamStep, DiamCost, Length);
                    SaveButton.IsEnabled = false;
                    MessageBox.Show(Tools.ChangesSaved, "Сохранение", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch
                {
                    MessageBox.Show(Tools.ChangesNotSaved, "Сохранение", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(validateMsg, "Сохранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Button_Delete_Click(object sender, RoutedEventArgs e)
        {
            switch (GridTab.SelectedIndex)
            {
                case 0: //Активная вкладка Диаметр – Шаг
                    DeleteRowFromDataTable(DiamStep, DiamStepView.SelectedIndex);
                    CreateFirstRow();
                    RecalcNum(DiamStep);
                    _selectedDiamStepRow = null;
                    break;
                case 1: //Активная вкладка Диаметр – Цена
                    DeleteRowFromDataTable(DiamCost, DiamCostView.SelectedIndex);
                    CreateFirstRow();
                    RecalcNum(DiamCost);
                    _selectedDiamCostRow = null;
                    break;
                case 2: //Активная вкладка Длина
                    DeleteRowFromDataTable(Length, LengthView.SelectedIndex);
                    CreateFirstRow();
                    RecalcNum(Length);
                    _selectedLengthRow = null;
                    break;
            }

            SaveButton.IsEnabled = true;
        }

        private void Button_Add_Click(object sender, RoutedEventArgs e)
        {
            DataRowView newRowView = null;

            switch (GridTab.SelectedIndex)
            {
                case 0: // Активная вкладка Диаметр – Шаг
                    var newRowDiamStep = DiamStep.NewRow();
                    DiamStep.Rows.Add(newRowDiamStep);
                    RecalcNum(DiamStep);
                    newRowView = DiamStepView.Items.OfType<DataRowView>().LastOrDefault();
                    _selectedDiamStepRow = newRowView;
                    break;
                case 1: // Активная вкладка Диаметр – Цена
                    var newRowDiamCost = DiamCost.NewRow();
                    DiamCost.Rows.Add(newRowDiamCost);
                    RecalcNum(DiamCost);
                    newRowView = DiamCostView.Items.OfType<DataRowView>().LastOrDefault();
                    _selectedDiamCostRow = newRowView;
                    break;
                case 2: // Активная вкладка Длина
                    var newRowLength = Length.NewRow();
                    Length.Rows.Add(newRowLength);
                    RecalcNum(Length);
                    newRowView = LengthView.Items.OfType<DataRowView>().LastOrDefault();
                    _selectedLengthRow = newRowView;
                    break;
            }

            if (newRowView != null)
            {
                switch (GridTab.SelectedIndex)
                {
                    case 0:
                        DiamStepView.SelectedItem = newRowView;
                        DiamStepView.ScrollIntoView(newRowView);
                        DiamStepView.Focus();
                        break;
                    case 1:
                        DiamCostView.SelectedItem = newRowView;
                        DiamCostView.ScrollIntoView(newRowView);
                        DiamCostView.Focus();
                        break;
                    case 2:
                        LengthView.SelectedItem = newRowView;
                        LengthView.ScrollIntoView(newRowView);
                        LengthView.Focus();
                        break;
                }
            }

            SaveButton.IsEnabled = true;
        }

        //Вспомогательный метод для пересчета номеров строк таблиц в ui
        private void RecalcNum(DataTable dataTable)
        {
            int num = 1;
            foreach (DataRow row in dataTable.Rows)
            {
                row["Num"] = num;
                num++;
            }
        }

        //Вспомогательный метод для удаления строки из таблицы
        private void DeleteRowFromDataTable(DataTable dataTable, int rowNum)
        {
            if (rowNum != -1)
            {
                dataTable.Rows.RemoveAt(rowNum);
            }
        }

        //Запрещаем ввод не чисел и нуля как первого элемента для таблицы Длина
        private void DataGrid_PreviewInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = e.OriginalSource as TextBox;
            string currentText = textBox?.Text;

            if (!Tools.IsInt(e.Text) || (e.Text == "0" && (currentText == null || currentText.Length == 0)) || (e.Text.Contains(' ')))
            {
                e.Handled = true;
            }
        }

        //Отслеживаем изменение значения в ячейке чтобы активировать сохранение
        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var editedText = (e.EditingElement as TextBox)?.Text;
            if (oldCellValue?.ToString() != editedText)
            {
                SaveButton.IsEnabled = true;
            }
        }

        //Запоминаем начальное значение чтобы корректно активировать сохранение
        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid != null && e.Row.Item is DataRowView row)
            {
                oldCellValue = row[e.Column.DisplayIndex];
            }
        }

        //Подписываемся на событие PreviewInput для всех TextBox в ячейках
        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.PreviewTextInput += DataGrid_PreviewInput;
        }
    }
}