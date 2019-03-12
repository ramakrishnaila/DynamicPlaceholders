using log4net;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Layouts;
using Sitecore.sitecore.admin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sitecore.Web.sitecore.admin
{
    public partial class FixDynamicPlaceholders : AdminPage
    {
        private const string ItemsWithPresentationDetailsQuery =
              "{0}//*[@__Renderings != '' or @__Final Renderings != '']";
        private const string DatabaseName = "master";
        private const string DefaultDeviceId = "{FE5D7FDF-89C0-4D99-9AA3-B5FBD009C9F3}";
        private const string StartItemId = "7F6CDD80-4BA0-4840-8753-B7B0A1353A91";
        private static readonly ILog _log = LogManager.GetLogger("Brother.DataMigration");
        protected void Page_Load(object sender, EventArgs e)
        {
            var result = Iterate();
            OutputResult(result);
        }

        private void OutputResult(Dictionary<Item, List<KeyValuePair<string, string>>> result)
        {
            Response.ContentType = "text/html";

            Response.Write($"<h1>{result.Count} items processed</h1>");
            foreach (var pair in result)
            {
                Response.Write($"<h3>{pair.Key.Paths.FullPath}</h3>");

                foreach (var kvp in pair.Value)
                {
                    if (kvp.Key != kvp.Value)
                    {
                        Response.Write($"<div>{kvp.Key} ==> {kvp.Value}</div>");
                    }
                }
            }
        }

        public Dictionary<Item, List<KeyValuePair<string, string>>> Iterate()
        {
            var result = new Dictionary<Item, List<KeyValuePair<string, string>>>();
            var master = Factory.GetDatabase(DatabaseName);
           Item  startItem = master.GetItem(StartItemId);
            var items = master.SelectItems(string.Format(ItemsWithPresentationDetailsQuery,
                        startItem.Paths.FullPath));

            var layoutFields = new[] { FieldIDs.LayoutField, FieldIDs.FinalLayoutField };

            foreach (var item in items)
            {
                foreach (var layoutField in layoutFields)
                {
                    var changeResult = ChangeLayoutFieldForItem(item, item.Fields[layoutField]);

                    if (changeResult.Any())
                    {
                        if (!result.ContainsKey(item))
                        {
                            result.Add(item, changeResult);
                        }
                        else
                        {
                            result[item].AddRange(changeResult);
                        }
                    }
                }
            }

            return result;
        }

        private List<KeyValuePair<string, string>> ChangeLayoutFieldForItem(Item currentItem, Field field)
        {

            var result = new List<KeyValuePair<string, string>>();

            string xml = LayoutField.GetFieldValue(field);
            try
            {
          if (!string.IsNullOrWhiteSpace(xml))
            {
                LayoutDefinition details = LayoutDefinition.Parse(xml);

                var device = details.GetDevice(DefaultDeviceId);
                DeviceItem deviceItem = currentItem.Database.Resources.Devices["Default"];

                RenderingReference[] renderings = currentItem.Visualization.GetRenderings(deviceItem, false);

                    bool needUpdate = false;
                    foreach (RenderingDefinition rendering in device.Renderings)
                    {
                        if (!string.IsNullOrWhiteSpace(rendering.Placeholder))
                        {
                            var newPlaceholder = rendering.Placeholder;
                            string placeHolderRegex = "([0-9a-f]{8}[-][0-9a-f]{4}[-][0-9a-f]{4}[-][0-9a-f]{4}[-][0-9a-f]{12})$";
                            foreach (Match match in Regex.Matches(newPlaceholder, placeHolderRegex, RegexOptions.IgnoreCase))
                            {
                                var renderingId = match.Value;
                                var newRenderingId = "-{" + renderingId.Substring(0) + "}-0";
                                newPlaceholder = newPlaceholder.Replace("_" + match.Value, newRenderingId);
                                needUpdate = true;
                            }
                            if(newPlaceholder.Contains("_-{"))
                            {
                                newPlaceholder = newPlaceholder.Replace("_-{", "-{");
                                needUpdate = true;
                            }
                            //newPlaceholder = newPlaceholder.Replace("_-", "-");
                            rendering.Placeholder = newPlaceholder;
                        }
                    }

                    if (needUpdate)
                    {
                        string newXml = details.ToXml();

                        using (new EditContext(currentItem))
                        {
                            LayoutField.SetFieldValue(field, newXml);
                        }
                        result.Add(new KeyValuePair<string, string>(currentItem.ID.ToString(), currentItem.Name));
                        //write it to log
                        _log.Info("Updated placeholder for " + currentItem.ID + " - Name - " + currentItem.Name);
                    }

                }
            }
            catch (Exception ex)
            {

                Sitecore.Diagnostics.Log.Error("UpcomingEventsandWebinarsController.Default", ex);

            }
            return result;
        }

        private string FixPlaceholderKey(string renderingInstancePlaceholder, IEnumerable<KeyValuePair<string, string>> map)
        {
            var value = renderingInstancePlaceholder;
            
            foreach (var oldValue in map)
            {
                try
                {
                    value = Regex.Replace(value, oldValue.Key, Guid.Parse(oldValue.Value).ToString(), RegexOptions.IgnoreCase);
                }
                catch (Exception ex)
                {
                    Sitecore.Diagnostics.Log.Error("FixPlaceholders Key", renderingInstancePlaceholder + "and old values are " + oldValue.Key + " and " + oldValue.Value);
                }
            }
            return value;
        }
     
    }
}