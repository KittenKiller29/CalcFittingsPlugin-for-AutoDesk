using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;

//В этом файле описан класс для взаимодействия с бд данных по арматуре
namespace CalcFittingsPlugin
{
    class DataBase
    {
        static private string LastMessage;

        static private string DBPath;

        //Загружает из бд данные по арматуре плагина
        public void LoadAllData()
        {
            
        }
        
        //Обновляет в бд данные по арматуре плагина
        public void UpdateAllData()
        {
            LastMessage = Tools.SucUpdateDB;
        }

        //Метод доступа к последнему сообщению от бд
        public string GetInfMessage()
        {
            return LastMessage;
        }

        public DataBase()
        {
            DBPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Tools.PluginName,
                Tools.DataBaseName
                );
            LoadDBFile();
        }
        //Проверка на существование и создание бд
        private void LoadDBFile()
        {
            string directoryPath = Path.GetDirectoryName(DBPath);
            Directory.CreateDirectory(directoryPath);
            if (!File.Exists(DBPath))
            {
                try
                {
                    SQLiteConnection.CreateFile(DBPath);
                    LastMessage = Tools.CreateDBFile;
                }
                catch
                {
                    LastMessage = Tools.ErrCreateDBFile;
                }
            }
            else
            {
                LastMessage = Tools.SucFindDBFile;
            }
        }

    }
}
