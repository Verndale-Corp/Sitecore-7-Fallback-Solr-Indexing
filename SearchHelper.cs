using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Linq;
using Sitecore.ContentSearch.Linq.Utilities;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.ContentSearch.Utilities;
using Sitecore.Data;
using Sitecore.Data.Items;
using System.Collections.Generic;
using System.Linq;

namespace Verndale.SharedSource.Helpers
{
    /// <summary>
    /// The SearchHelper class contains methods for searching for items in the Sitecore database
    /// </summary>
    public class SearchHelper
    {
        public static string FormatKeywordWithSpacesForSearch(string searchString)
        {
            var formattedSearchString = searchString;

            if (!string.IsNullOrEmpty(searchString))
                formattedSearchString = (searchString.Contains(" ") || searchString.Contains("-")) &&
                                       (!searchString.StartsWith("\"") && !searchString.EndsWith("\""))
                ? "\"" + searchString + "\""
                : searchString;

            return formattedSearchString;
        }

        // will set the index and the databasename appropriately
        private static string GetIndexBasedOnContext(string currentIndexName, out string databaseName)
        {
            // default to whatever the index that was used in the call from the code
            string contextIndexName = currentIndexName;
            databaseName = "web";

            // if we are performing this search in anything other than normal, eg page editor, preview, debug, etc, 
            // then the index should be the main sitecore master index
            if (!Sitecore.Context.PageMode.IsNormal)
            {
                contextIndexName = "sitecore_master_index";
                databaseName = "master";
            }
            else
            {
                // webdatabasename will be set to web or liveweb, assumption is we are always making the call with 'web' 
                // and then in here it will be replaced based on environment
                var webDatabaseName = Sitecore.Configuration.Settings.GetSetting("Search.WebDatabaseName");
                if (!string.IsNullOrEmpty(webDatabaseName))
                {
                    contextIndexName = contextIndexName.Replace("web", webDatabaseName);
                    databaseName = webDatabaseName;
                }
            }

            return contextIndexName;
        }

        /// <summary>
        /// Searches for items.
        /// </summary>
        public static List<Item> GetItems
            (string indexName, string language, string templateGuidFilter, string locationGuidFilter,
            string fullTextQuery = "", List<Refinement> refinementFilter = null,
            List<PrioritizedField> overrideContentPrioritizedFieldList = null)
        {
            var query = GetQueryableResults(indexName, language, templateGuidFilter, locationGuidFilter, fullTextQuery, refinementFilter, overrideContentPrioritizedFieldList);
            return query != null ? query.Select(toItem => toItem.GetItem()).ToList() : null;
        }

        public static IQueryable<SearchResultItem> GetQueryableResults
            (string indexName, string language, string templateGuidFilter,
            string locationGuidFilter, string fullTextQuery = "", List<Refinement> refinementFilter = null, 
            List<PrioritizedField> overrideContentPrioritizedFieldList = null)
        {
            string databaseName;
            indexName = GetIndexBasedOnContext(indexName, out databaseName);

            using (var context = ContentSearchManager.GetIndex(indexName).CreateSearchContext())
            {
                // use the predicate builder to build out criteria for location guid, appended with 'OR'
                var locationSearch = PredicateBuilder.True<SearchResultItem>();
                if (!string.IsNullOrEmpty(locationGuidFilter))
                {
                    locationSearch = locationGuidFilter.Split('|').Select(SitecoreHelper.ItemMethods.GetItemFromGUID)
                        .Aggregate(locationSearch, (current, locationItem) => current.Or(t => t.Paths.Contains(locationItem.ID)));
                }

                // use the predicate builder to build out criteria for template guid, appended with 'OR'
                var templateSearch = PredicateBuilder.True<SearchResultItem>();
                if (!string.IsNullOrEmpty(templateGuidFilter))
                {
                    templateSearch = templateGuidFilter.Split('|').Select(ID.Parse).
                        Aggregate(templateSearch, (current, newTemplateGuid) => current.Or(t => t.TemplateId == newTemplateGuid));
                }

                var termSearch = PredicateBuilder.True<SearchResultItem>();
                if (!string.IsNullOrEmpty(fullTextQuery))
                {
                    if (fullTextQuery.Contains("\"") || (overrideContentPrioritizedFieldList != null && overrideContentPrioritizedFieldList.Any()))
                    {
                        // SOLR will add the quotes for you, just needed to add them ahead of time to designate it should be exact phrase
                        fullTextQuery = fullTextQuery.Replace("\"", "");

                        if (overrideContentPrioritizedFieldList != null && overrideContentPrioritizedFieldList.Any())
                        {
                            // if there are prioritized fields, use them instead of the Content field, append together with 'Or'
                            // boost them to whatever value was set to make sure the other fields are prioritized
                            foreach (var prioritizedField in overrideContentPrioritizedFieldList)
                            {
                                var thisSearchText = fullTextQuery;

                                // if it is solr and an _t (text) field, must append a space to the beginning.  
                                // This will force solr to put the keyword in quotes and do an exact match search, 
                                // which we need if there are special characters like dashes
                                // if it is solr and an _s (string) field, it is going to match on case, we will assume lowercase
                                if (prioritizedField.FieldName.EndsWith("_t"))
                                    thisSearchText = " " + thisSearchText;
                                else if (prioritizedField.FieldName.EndsWith("_s"))
                                    thisSearchText = thisSearchText.ToLower();
                                termSearch = termSearch.Or(t =>
                                            t[prioritizedField.FieldName].Equals(thisSearchText)
                                                .Boost(prioritizedField.BoostValue));

                                // if search should also be performed for nonplural version of search term
                                // then get the singular version, and if it had been plural (a different singular version was returned)
                                // append another 'or' search for this field for that singular value
                                if (prioritizedField.IncludeNonPluralSearch)
                                {
                                    var singularValue = Verndale.SharedSource.Utilities.TextUtilityStatic.GetSingular(thisSearchText);
                                    if (thisSearchText.ToLower() != singularValue.ToLower())
                                    {
                                        termSearch = termSearch.Or(t =>
                                            t[prioritizedField.FieldName].Equals(singularValue)
                                                .Boost(prioritizedField.BoostValue));
                                    }
                                }
                            }
                        }
                        else
                        {
                            termSearch = termSearch.And(t => t.Content.Equals(fullTextQuery));
                        }

                    }
                    else
                    {
                        foreach (var term in fullTextQuery.Split(' '))
                        {
                            var newTerm = term;
                            termSearch = termSearch.And(t => t.Content.Equals(newTerm));

                            // TODO: Implement a way to leverage wildcard
                            // something like below should be used with wildcard (*, ?) searches
                            // termSearch = termSearch.And(r => r["headline_t"].MatchWildcard(keyword));
                        }
                    }
                }

                // TODO: Add in DateRange filters

                // TODO: Add in Refinements, a dictionary of Fieldname/Value that should be added as criteria
                // also allow to specify whether these are appended together with an OR or an AND
                var refinementSearch = PredicateBuilder.True<SearchResultItem>();
                if (refinementFilter != null)
                {
                    // loop through non-facet refinements and non-or refinements, will append with 'and'
                    // use .Equals, will find the exact phrase or value within the text of the field
                    foreach (var refinement in refinementFilter.Where(x => !x.IsFacetRefinement && !x.IsOr))
                    {
                        var fieldName = refinement.FieldName.ToLowerInvariant();
                        var fieldValue = IdHelper.ProcessGUIDs(refinement.Value);
                        refinementSearch = refinementSearch.And(t => t[fieldName].Equals(fieldValue));
                    }

                    // loop through non-facet refinements that are 'or', 
                    // will append with 'or' and then add on to main refinements with 'and'
                    var orSearch = PredicateBuilder.True<SearchResultItem>();
                    bool hasOr = false;
                    foreach (var refinement in refinementFilter.Where(x => !x.IsFacetRefinement && x.IsOr))
                    {
                        var fieldName = refinement.FieldName.ToLowerInvariant();
                        var fieldValue = IdHelper.ProcessGUIDs(refinement.Value);
                        orSearch = orSearch.Or(t => t[fieldName].Equals(fieldValue));
                        hasOr = true;
                    }
                    if (hasOr)
                        refinementSearch = refinementSearch.And(orSearch);

                    // loop through facet refinements, each grouping of facet values will be appended with an 'And'
                    // however if multiple values are selected within a facet grouping, they should be appended with an 'Or'
                    foreach (var refinement in refinementFilter.Where(x => x.IsFacetRefinement))
                    {
                        var fieldName = refinement.FieldName.ToLowerInvariant();
                        var fieldValue = IdHelper.ProcessGUIDs(refinement.Value);
                        var facetRefinementValues = fieldValue.Split('|');

                        if (facetRefinementValues.Any())
                        {
                            var facetSearch = PredicateBuilder.True<SearchResultItem>();
                            foreach (var facetRefinementValue in facetRefinementValues)
                            {
                                if (!string.IsNullOrWhiteSpace(facetRefinementValue))
                                {
                                    string currentValue = facetRefinementValue;
                                    facetSearch = facetSearch.Or(t => t[fieldName].Equals(currentValue));
                                }
                            }
                            refinementSearch = refinementSearch.And(facetSearch);
                        }
                    }
                }

                // TODO: Add in boosting

                // if SOLR throws exception, add not null to index field
                if (context.Index.FieldNameTranslator != null)
                {
                    // start building out the query, specifying the language
                    var query = context.GetQueryable<SearchResultItem>().Where(x => x.Language == language);

                    // set databasename
                    query = query.Where(x => x.DatabaseName == databaseName);

                    // if locationguid was set, add in the location query
                    if (!string.IsNullOrEmpty(locationGuidFilter))
                        query = query.Where(locationSearch);

                    // if templateguid was set, add in the template query
                    if (!string.IsNullOrEmpty(templateGuidFilter))
                        query = query.Where(templateSearch);

                    // if fulltextquery was set, add in the termSearch query
                    if (!string.IsNullOrEmpty(fullTextQuery))
                        query = query.Where(termSearch);

                    // if refinements were set, add in the refinementSearch query
                    if (refinementFilter != null && refinementFilter.Count > 0)
                        query = query.Where(refinementSearch);

                    
                    return query;
                }
            }

            return null;
        }

        
    }

    public class Refinement
    {
        public string FieldName { get; set; }
        public string Value { get; set; }
        public bool IsOr { get; set; }
        public bool IsFacetRefinement { get; set; }
    }

    public class PrioritizedField
    {
        public string FieldName { get; set; }
        public float BoostValue { get; set; }
        public bool IncludeNonPluralSearch { get; set; }
    }

    public class Facet
    {
        public string FieldName { get; set; }
        public string PipeDelimitedFacetValues { get; set; }
        public string SpecFacetName { get; set; }
    }
}
