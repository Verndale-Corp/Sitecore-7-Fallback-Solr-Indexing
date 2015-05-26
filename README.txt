Verndale Sitecore 7 Search SOLR Index Updates for Sitecore Language Fallback

Purpose
Enhance the Sitecore 7 Solr Document Builder to take language fallback into account when generating indexes.

Compatibility
This is for use with Sitecore 7
It has been verified to work with Sitecore 7.2 update 2
And it assumes you are using the latest version of the Partial Language Fallback Module
http://marketplace.sitecore.net/en/Modules/Language_Fallback.aspx

How to build code and deploy the solution
1. When using the fallback module, make sure to compile the source code to get the dll, do not use the one within the package.  Make sure to compile against the version of the Sitecore Kernel and Client dlls that you will be using for your main project

2. Add reference to the Sitecore.SharedSource.PartialLanguageFallback.dll that you compiled

3. Put the CustomSolrDocumentBuilder.cs and IndexHelper.cs in your project

4. Look at the Sitecore.SharedSource.PartialLanguageFallback.config file for the patch to the documentBuilderType attribute.  Apply the same patch in your project


Testing
1. There should be more than one language in your site 
2. Add a version to an item for each language
3. Add content to the fallback language and no content to the language falling back
4. Publish to both languages
5. Perform Index search on the language falling back, the item should come up as a result

Review the blog series about Partial Language Fallback on Sitecore, http://www.sitecore.net/en-gb/Learn/Blogs/Technical-Blogs/Elizabeth-Spranzani.aspx
