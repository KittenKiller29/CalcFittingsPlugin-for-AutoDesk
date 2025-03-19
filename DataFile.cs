using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//В этом файле описан класс для взаимодействия с JSON файлом данных по арматуре
namespace CalcFittingsPlugin
{
    public static class DataFile
    {
        static private string LastMessage;

        //Загружает из JSON данные по арматуре плагина
        public static void LoadAllData()
        {
            
        }
        
        //Обновляет в JSON данные по арматуре плагина
        public static void UpdateAllData()
        {
            
        }

        //Метод доступа к последнему сообщению от файла данных
        public static string GetInfMessage()
        {
            return LastMessage;
        }

        //Проверка на существование и создание JSON
        private static void LoadJSONFile()
        {
            
        }

    }
}
