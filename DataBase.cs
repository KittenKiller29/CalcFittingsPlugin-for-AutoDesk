using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//В этом файле описан класс для взаимодействия с бд данных по арматуре
namespace CalcFittingsPlugin
{
    class DataBase
    {
        static public string LastMessage;

        public void GetAllData()
        {
            
        }

        public void UpdateAllData()
        {
        
        }

        public string GetInfMessage()
        {
            return LastMessage;
        }

        public DataBase()
        {
            LoadDBFile();
        }

        private void LoadDBFile()
        {
            


        }

    }
}
