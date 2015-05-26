using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Sitecore.Configuration;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.SharedSource.PartialLanguageFallback.Extensions;

namespace Verndale.SharedSource.Helpers
{
    /// <summary>
    /// The IndexHelper class contains a method for recursively getting sitecore fallback item values.
    /// </summary>
    public class IndexHelper
    {
        // recursive method to keep checking for value in fallback items 
        // until we find a value or until we reach an item that no longer falls back
        public static string GetSitecoreFallbackValue(Item item, Field fld)
        {
            string currentValue = "";
            try
            {

                // Cannot check 'item.Fields[fld.ID].HasValue' first, because it returns false
                // even though '.Value' will return a value in situation where standard values on the template comes into play
                if (item.Fields[fld.ID] != null)
                {
                    var value = item.Fields[fld.ID].Value;
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }

                var fallbackItem = item.GetFallbackItem();
                if (fallbackItem != null)
                {
                    //Recursive call to get the item that has value for the particular field.
                    currentValue = GetSitecoreFallbackValue(fallbackItem, fld);
                }

            }
            catch (Exception)
            {
                // TODO: Need comment
            }
            return currentValue;
        }

    }
}