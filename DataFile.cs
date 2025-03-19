using System;
using System.IO;
using System.Data;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

//В этом файле описан класс для взаимодействия с JSON файлом данных по арматуре
namespace CalcFittingsPlugin
{
    public static class DataFile
    {
        //Загружает из JSON данные по арматуре плагина в двумерные массивы
        public static void LoadAllData(ArrayList DiamStep, ArrayList DiamCost, ArrayList Length)
        {
            if (!File.Exists(GetDataFilePath())) return;
            //Очищаем полученные массивы, так как данные будут полностью перезагружены из json
            DiamStep.Clear();
            DiamCost.Clear();
            Length.Clear();
        }

        //Загружает из JSON данные по арматуре плагина в таблицы данных
        public static void LoadAllData(DataTable DiamStep, DataTable DiamCost, DataTable Length)
        {
            if (!File.Exists(GetDataFilePath())) return;
            //Очищаем полученные таблицы, так как данные будут полностью перезагружены из json
            DiamStep.Clear();
            DiamCost.Clear();
            Length.Clear();
            

        }

        //Обновляет в JSON данные по арматуре плагина
        public static void UpdateAllData()
        {
            
        }

        //Возвращает путь до файла данных JSON
        private static string GetDataFilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Tools.PluginName,
                Tools.DataFileName
            );
        }
        //Проверка на существование и создание JSON
        public static string ValidateJSONFile()
        {
            try
            {
                string directory = Path.GetDirectoryName(GetDataFilePath());

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(GetDataFilePath()))
                {
                    var data = new
                    {
                        DiamStep = new int [0][],
                        DiamCost = new int [0][],
                        Length = new int [0][]
                    };

                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);

                    File.WriteAllText(GetDataFilePath(), json);

                    return Tools.CreateJSONFile;
                }
                else
                {
                    return Tools.SucFindJSONFile;
                }
            }
            catch
            {
                return Tools.ErrCreateJSONFile;
            }
        }

    }
}
