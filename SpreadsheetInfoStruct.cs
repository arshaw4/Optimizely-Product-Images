using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoImageCheck
{
    public class SpreadsheetInfoStruct
    {
        public string erpNumber {  get; set; }
        public string brand {  get; set; }
        public string id { get; set; }
        public string errorMessage { get; set; }
        public string imgURL { get; set; }
        public string prodURL { get; set; }
        public string isDiscontinued { get; set; }
        
        public override string ToString()
        {
            return $"{erpNumber}: {brand}: {id}: {errorMessage}: {imgURL}: {prodURL}: {isDiscontinued}";
        }
    }
}
