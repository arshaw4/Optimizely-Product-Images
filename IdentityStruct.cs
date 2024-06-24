using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoImageCheck
{
    public class IdentityStruct
    {
        public string grant_type { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public string scope { get; set; }

        public override string ToString()
        {
            return $"grant_type={grant_type}&username={username}&password={password}&scope={scope}";
        }

    }
}