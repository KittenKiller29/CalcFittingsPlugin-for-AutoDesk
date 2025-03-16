using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//В этом файле описан вспомогательный класс для используемых констант
namespace CalcFittingsPlugin
{
    public static class Constants
    {
        public const string DataBaseName = "FitData.db";
        public const string CreateDBFile = "Создан файл базы данных FitData.db";
        public const string SucFindDBFile = "Файл базы данных FitData.db обнаружен";
        public const string SucLoadDataDB = "Данные из FitData.db успешно загружены";
        public const string EmptyDiamStep = "Не удалось выполнить расчет, отсутствуют данные Диаметр – Шаг";
        public const string EmptyDiamCost = "Не удалось выполнить расчет, отсутствуют данные Диаметр – Цена";
        public const string EmptyLength = "Не удалось выполнить расчет, отсутствуют данные Длина";
        public const string ErrDataBase = "Не удалось получить доступ к данным в FitData.db или к файлу базы данных";
        public const string SucUpdateDB = "Данные в FitData.db обновлены";
    }
    
}