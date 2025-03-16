using System;
using System.Text.RegularExpressions;

namespace CalcFittingsPlugin
{
    public static class Tools
    {
        public const string DataBaseName = "FitData.db";
        public const string CreateDBFile = "Создан файл базы данных FitData.db";
        public const string SucFindDBFile = "Файл базы данных FitData.db обнаружен";
        public const string ErrCreateDBFile = "Не удалось создать файл базы данных FitData.db";
        public const string EmptyDiamStep = "Не удалось выполнить расчет, отсутствуют данные Диаметр – Шаг";
        public const string EmptyDiamCost = "Не удалось выполнить расчет, отсутствуют данные Диаметр – Цена";
        public const string EmptyLength = "Не удалось выполнить расчет, отсутствуют данные Длина";
        public const string ErrDataBase = "Не удалось получить доступ к данным в FitData.db или к файлу базы данных";
        public const string SucUpdateDB = "Данные обновлены";
        public const string PluginName = "CalcFittingsPlugin";

        // Преобразует текст в Лог-сообщение [время]: Текст
        public static string CreateLogMessage(string text)
        {
            DateTime now = DateTime.Now;
            return "[" + now.ToString("HH:mm:ss") + "]: " + text + "\n";
        }
        //Функция для проверки регуляркой, является ли введенный символ числом
        public static bool IsInt(string text)
        {
            Regex regex = new Regex("[^0-9]+");
            return !regex.IsMatch(text);
        }
    }
}