﻿using System;
using System.IO;
using System.Data;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

//В этом файле описан класс для взаимодействия с JSON файлом данных по арматуре
namespace CalcFittingsPlugin
{
    public static class DataFile
    {
        //Загружает из JSON данные по арматуре плагина в таблицы данных
        public static void LoadAllData(DataTable DiamStep, DataTable DiamCost, DataTable Length)
        {
            if (!File.Exists(GetDataFilePath())) return;
            //Очищаем полученные таблицы, так как данные будут полностью перезагружены из json
            DiamStep.Clear();
            DiamCost.Clear();
            Length.Clear();

            // Читаем JSON из файла
            string jsonData = File.ReadAllText(GetDataFilePath());

            // Парсим JSON в объект JObject
            JObject jsonObject = JObject.Parse(jsonData);

            // Загружаем данные в таблицу DiamStep
            LoadTableFromJson(jsonObject, "DiamStep", DiamStep, new string[] { "Diam", "Step" });

            // Загружаем данные в таблицу DiamCost
            LoadTableFromJson(jsonObject, "DiamCost", DiamCost, new string[] { "Diam", "Cost" });

            // Загружаем данные в таблицу Length
            LoadTableFromJson(jsonObject, "Length", Length, new string[] { "Length" });
        }

        private static void LoadTableFromJson(JObject jsonObject, string tableName, DataTable table, string[] columns)
        {
            // Получаем массив данных для таблицы из JSON
            JArray jsonArray = (JArray)jsonObject[tableName];

            // Перебираем элементы массива
            foreach (JObject item in jsonArray)
            {
                // Создаем новую строку для таблицы
                DataRow row = table.NewRow();

                // Заполняем строку данными из JSON
                foreach (string column in columns)
                {
                    row[column] = item[column].ToObject<object>();
                }

                // Добавляем строку в таблицу
                table.Rows.Add(row);
            }
        }

        //Обновляет в JSON данные по арматуре плагина
        public static void UpdateAllData(DataTable DiamStep, DataTable DiamCost, DataTable Length)
        {
            //Создание директории для файла для исключения возможных ошибок с отсутствием файла
            string directory = Path.GetDirectoryName(GetDataFilePath());
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            //Создаем копии таблиц без столбцов Num и сериализируем их в JSON
            DataTable DiamStepSerialize = DiamStep.Copy();
            DataTable DiamCostSerialize = DiamCost.Copy();
            DataTable LengthSerialize = Length.Copy();

            DiamStepSerialize.Columns.Remove("Num");
            DiamCostSerialize.Columns.Remove("Num");
            LengthSerialize.Columns.Remove("Num");

            var data = new
            {
                DiamStep = DiamStepSerialize,
                DiamCost = DiamCostSerialize,
                Length = LengthSerialize
            };

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(GetDataFilePath(), json);

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
