using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using System.Collections.Generic;

namespace GoldPriceAlertWinForms.Utils
{
    public static class TemplateHelper
    {
        public static string Render(string template, IReadOnlyDictionary<string, string> tokens)
        {
            if (template == null) return "";
            string result = template;

            foreach (var kv in tokens)
                result = result.Replace("{" + kv.Key + "}", kv.Value ?? "", StringComparison.OrdinalIgnoreCase);

            return result;
        }
    }
}

