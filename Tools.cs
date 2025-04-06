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
        public const string ChangesSaved = "Изменения успешно сохранены!";
        public const string ChangesNotSaved = "Не удалось сохранить изменения!\n";
        public const string ForDiamStep = "Для таблицы Диаметр – Шаг:\n";
        public const string ForDiamCost = "Для таблицы Диаметр – Цена:\n";
        public const string ForLength = "Для таблицы Длина:\n";
        public const string InvalideData = "Невалидные данные:\n";
        public const string SucLoadFit = "Загрузка армирования: Успешно.";
        public const string ErrLoadFit = "Загрузка армирования: Ошибка.";
        public const string ParseStart = "Начата загрузка армирования из .csv файла.";
        public const string ParceEnd = "Загрузка армирования из .csv файла завершена.";
        public const string ParceErr = "Не удалось прочитать данные об армировании из .csv файла.";
        public const string CalcStart = "Расчет запущен.";
        public const string CalcErr = "Не удалось выполнить расчет.";
        public const string CalcSuc = "Расчет выполнен успешно.";
        public const int HeadersCount = 10;
        public static readonly string[] HeadersTemplate = { "Тип", "Номер", "Координата X узлов", "Координата Y узлов", "Координата Z центр", "Координата Z минимум", "As1X", "As2X", "As3Y", "As4Y" };

        // Преобразует текст в Лог-сообщение [время]: Текст
        public static string CreateLogMessage(string text)
        {
            DateTime now = DateTime.Now;
            return "[" + now.ToString("HH:mm:ss") + "]: " + text + "\n";
        }
        //Функция для проверки регуляркой, является ли введенный символ числом
        public static bool IsInt(string text)
        {
            Regex regex = new Regex(@"^\d+$");
            return regex.IsMatch(text);
        }
          
    }
}
