using System;
using System.IO;
using System.Text.RegularExpressions;

namespace CalcFittingsPlugin
{
    public static class Tools
    {
        public const string DataFileName = "FitData.json";
        public const string CreateJSONFile = "Создан файл данных FitData.json";
        public const string SucFindJSONFile = "Файл данных FitData.json обнаружен";
        public const string ErrCreateJSONFile = "Не удалось создать файл данных FitData.json";
        public const string EmptyDiamStep = "Не удалось выполнить расчет, отсутствуют данные Диаметр – Шаг";
        public const string EmptyDiamCost = "Не удалось выполнить расчет, отсутствуют данные Диаметр – Цена";
        public const string EmptyLength = "Не удалось выполнить расчет, отсутствуют данные Длина";
        public const string ErrJSON = "Не удалось получить доступ к данным в FitData.json";
        public const string SucUpdateJSON = "Данные обновлены";
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
