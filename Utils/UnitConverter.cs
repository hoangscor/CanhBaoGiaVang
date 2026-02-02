using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GoldPriceAlertWinForms.Models;

namespace GoldPriceAlertWinForms.Utils
{
    public static class UnitConverter
    {
        // 1 troy ounce = 31.1034768 grams
        public const double GramsPerTroyOunce = 31.1034768;

        public static double ToDisplayUnit(double pricePerOunce, PriceUnit displayUnit)
            => displayUnit == PriceUnit.Gram ? pricePerOunce / GramsPerTroyOunce : pricePerOunce;

        public static string UnitLabel(PriceUnit unit) => unit == PriceUnit.Gram ? "g" : "oz";
    }
}
