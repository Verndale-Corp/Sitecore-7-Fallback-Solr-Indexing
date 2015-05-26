using System.Collections.Generic;
using System.Linq;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Abstractions;
using Sitecore.ContentSearch.Boosting;
using Sitecore.ContentSearch.ComputedFields;
using Sitecore.ContentSearch.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using Sitecore.SharedSource.PartialLanguageFallback.Extensions;
using Sitecore.SharedSource.PartialLanguageFallback.Managers;
using Sitecore.ContentSearch.SolrProvider;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Fields;
using Verndale.SharedSource.Helpers;

namespace Verndale.SharedSource.SitecoreProviders
{
    public class CustomSolrDocumentBuilder : SolrDocumentBuilder
    {
        private readonly SolrFieldMap fieldMap;
        private readonly SolrFieldNameTranslator fieldNameTranslator;
        private readonly CultureInfo culture;
        
        public CustomSolrDocumentBuilder(IIndexable indexable, IProviderUpdateContext context)
            : base(indexable, context)
        {
            this.fieldMap = context.Index.Configuration.FieldMap as SolrFieldMap;
            this.fieldNameTranslator = context.Index.FieldNameTranslator as SolrFieldNameTranslator;
            this.culture = indexable.Culture;
        }

        public override void AddField(IIndexableDataField field)
        {
            string name = field.Name;
            object fieldValue = this.Index.Configuration.FieldReaders.GetFieldValue(field);
            Sitecore.Data.Fields.Field thisField = (Sitecore.Data.Fields.Field)(field as SitecoreItemDataField);

            //UPDATED, Added By Verndale for Fallback
            //<!--ADDED FOR FALLBACK DEMO-->
            if (fieldValue == null || fieldValue is string && string.IsNullOrEmpty(fieldValue.ToString()) ||
                string.IsNullOrEmpty(thisField.Value))
            {
                // Get the Sitecore field for the Indexable Data Field (which is more generic) that was passed in
                // If the field is valid for fallback, then use the ReadFallbackValue method to try and get a value

                if (thisField.ValidForFallback())
                {
                    // FOUND THAT THIS IS NOT WORKING NOW FOR CHAINED LANGUAGES, NOT SURE WHY
                    // SO IN THE CASE OF fr-CA -> en-CA -> en, IT WASN'T BEING CONSISTENT, 
                    // ESPECIALLY IF STANDARD VALUES ON TEMPLATE WAS WHERE THE VALUE WAS ULTIMATELY COMING FROM
                    // ReadFallbackValue will get the fallback item for the current item 
                    // and will try to get the field value for it using fallbackItem[field.ID]
                    // Merely calling fallbackItem[field.ID] triggers the GetStandardValue method
                    // which has been overridden in the standard values provider override FallbackLanguageProvider
                    // which will in turn call ReadFallbackValue recursively until it finds a value or reaches a language that doesn't fallback 
                    //var tempfieldValue = FallbackLanguageManager.ReadFallbackValue(thisField, thisField.Item);

                    // get the field type based on the original object, will have to cast the object into the correct type from a string
                    Type fieldType = null;
                    if (fieldValue != null)
                        fieldType = fieldValue.GetType();
                        
                    // GetSitecoreFallbackValue is a recursive method that will keep getting the FallbackItem for the item
                    // until it finds one with a value, including retrieving it from standard values
                    fieldValue = IndexHelper.GetSitecoreFallbackValue(thisField.Item, thisField);

                    // it could be that the value is a list of guids, or a date, etc and SOLR stores those in a different way than just a string
                    if (fieldType != null && fieldValue != null && fieldValue is string && !string.IsNullOrEmpty(fieldValue.ToString()))
                    {
                        var thisFieldValue = fieldValue.ToString();

                        // if list, that case, load it into a string list and that will be the object used
                        if (fieldType.Name == "List`1")
                        {
                            var fieldValueList = new Sitecore.Text.ListString(thisFieldValue, '|');
                            if (fieldValueList != null && fieldValueList.Any())
                            {
                                List<string> idList = new List<string>();
                                foreach (string str1 in fieldValueList.Items)
                                {
                                    if (ID.IsID(str1))
                                    {
                                        string str2 = ShortID.Encode(str1).ToLowerInvariant();
                                        idList.Add(str2);
                                    }
                                }
                                if (idList.Any())
                                    fieldValue = idList;
                            }
                        }
                        else if (fieldType.Name == "Guid" && ID.IsID(thisFieldValue))
                        {
                            fieldValue = ShortID.Encode(thisFieldValue).ToLowerInvariant();
                        }
                        else if (fieldType.Name == "Boolean")
                        {
                            fieldValue = (thisFieldValue == "1") ? true : false;
                        }
                        else if (fieldType.Name == "DateTime")
                        {
                            DateTime thisFieldValueDate = SitecoreHelper.ItemRenderMethods.GetDateFromSitecoreIsoDate(thisFieldValue);
                            if (thisFieldValueDate != new DateTime())
                                fieldValue = thisFieldValueDate;
                        }
                        else if (fieldType.Name == "String")
                        {
                            fieldValue = thisFieldValue;
                        }
                    }
                        
                    // BELOW ARE EXAMPLES OF CASTING BACK AND FORTH FOR SITECORE FIELDS AND ITEMS TO INDEXABLE AND BACK
                    // -- Get Indexable from Sitecore Item
                    // SitecoreIndexableItem indexableFallbackItem = (SitecoreIndexableItem)fallbackItem;

                    // -- Get Sitecore Item from Indexable variable
                    // Item thisItem = (Item)(Indexable as SitecoreIndexableItem);

                    // -- Get Indexable field from indexable item's field by field id
                    // IIndexableDataField fallbackField = indexableFallbackItem.GetFieldById(field.ID);
                }
            }
                
            if (fieldValue == null || fieldValue is string && string.IsNullOrEmpty(fieldValue.ToString()))
                return;
            
            float num = BoostingManager.ResolveFieldBoosting(field) + this.GetFieldConfigurationBoost(name);

            // originally had been appending the culture onto the fieldname, but not sure why
            // this would result in two entries in the index document, eg: headline_t and headline_t_fr
            // the culture version seemed redundant unless you were always going to search based on the 'en' language 
            // and then just change the name of the field you were searching for the culture.
            //string indexFieldName = this.fieldNameTranslator.GetIndexFieldName(name, fieldValue.GetType(), this.culture);

            // this gets the name of the field that is in the solr index, it will have the type appended
            string indexFieldName = this.fieldNameTranslator.GetIndexFieldName(name, fieldValue.GetType());
            
            // if not a media item and it is a text field, append it onto the content field which contains a lot of text
            if (!this.IsMedia && IndexOperationsHelper.IsTextField(field))
                this.StoreField(Sitecore.Search.BuiltinFields.Content, fieldValue, true, new float?());
            
            // make call to store it
            this.StoreField(indexFieldName, fieldValue, false, new float?(num));
        }

        private void StoreField(string fieldName, object fieldValue, bool append = false, float? boost = null)
        {
            if (this.Index.Configuration.IndexFieldStorageValueFormatter != null)
                fieldValue = this.Index.Configuration.IndexFieldStorageValueFormatter.FormatValueForIndexStorage(fieldValue);
            
            if (append && this.Document.ContainsKey(fieldName) && fieldValue is string)
            {
                ConcurrentDictionary<string, object> document;
                string index;
                (document = this.Document)[index = fieldName] = (object)((string)document[index] + (object)" " + (string)fieldValue);
            }
            
            // if the fieldname is already in the document, no need to go on
            if (this.Document.ContainsKey(fieldName))
                return;

            if (boost.HasValue)
            {
                float? nullable = boost;
                if (((double)nullable.GetValueOrDefault() <= 0.0 ? 0 : (nullable.HasValue ? 1 : 0)) != 0)
                    fieldValue = (object)new SolrBoostedField(fieldValue, boost);
            }

            // add the field and value to the index document
            this.Document.GetOrAdd(fieldName, fieldValue);
            
            // this.settings.DefaultLanguage is not accessible here, so hard coded to 'en'
            //if (!this.fieldNameTranslator.HasCulture(fieldName) || this.settings.DefaultLanguage().StartsWith(this.culture.TwoLetterISOLanguageName))
            if (!this.fieldNameTranslator.HasCulture(fieldName) || this.culture.TwoLetterISOLanguageName == "en")
                return;

            // will never get here now, because we are no longer appending culture to the name of the field
            // if we had been, then above GetOrAdd call would have saved the field to the index with the culture applied
            // and then this would be necessary to save the field AGAIN to the index without the culture
            this.Document.GetOrAdd(this.fieldNameTranslator.StripKnownCultures(fieldName), fieldValue);
        }

        private float GetFieldConfigurationBoost(string fieldName)
        {
            SolrSearchFieldConfiguration fieldConfiguration = this.fieldMap.GetFieldConfiguration(fieldName) as SolrSearchFieldConfiguration;
            if (fieldConfiguration != null)
                return fieldConfiguration.Boost;
            else
                return 0.0f;
        }
    }
}
