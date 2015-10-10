﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DotNetUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Templator;

namespace DocGenerate
{
    public static class TemplatorHelpDoc
    {
        private static readonly TemplatorConfig Config = TemplatorConfig.DefaultInstance;

        public static string Title = "Templator";
        public static string Description = "-- An Advanced text templating engine";
        public static IList<Triple<string, string, IDictionary<string, object>[]>> Sections = new List<Triple<string, string, IDictionary<string, object>[]>>
        {
            new Triple<string, string, IDictionary<string, object>[]>("Get Started", "Search for 'Templator' in nuget package manager, current version 1.0.0.5", null),
            new Triple<string, string, IDictionary<string, object>[]>("Philosophy", "Try to Create a text processing engine with the ablity to produce all kinds of formated text using a unified input structure, with the ablity to be fully cusomized in order to overcome any possible symbol conflicts.", null),
            new Triple<string, string, IDictionary<string, object>[]>("Template Usage", "Simply put the a place holder at the position of the text which you what to output, put the desired value inside the input dictionary with the name of the holder as Key, further, the usage of the rich keywords will enable programmer to calculated/validate/re-format against the input value", null),
            new Triple<string, string, IDictionary<string, object>[]>("Syntax of a TextHolder", "With the format of {{HolderName}} or {{Category(HolderName)}} or {{Category(HolderName)[Keyword1(Param1),Keyword2()]}}, simply wrap the holder name with in the begin tag({{) and end tag(}}) will produce a TextHolder, the tags are all customizeable in the config object. See examples below:", GetSyntaxExamples().ToArray()),
            new Triple<string, string, IDictionary<string, object>[]>("Build phase validation", "Nuget Search for package 'TemplatorSyntaxBuildTask', install it to the project which contains your templates, the task will add a 'TemplatorConfig.xml' into the project and load configurations from it:", GetBuildTaskConfigurations()),
            new Triple<string, string, IDictionary<string, object>[]>("Editor SyntaxHighlighting", "Working in progress, The project 'TemplatorVsExtension' is going to provide syntax highlighting in visual studio. based on TemplatorConfig.xml in the project, in order to get less impact to vs performace in regular work, the extension will only try to parse the active document if the project contains a valid 'TemplatorConfig.xml'", null),
            new Triple<string, string, IDictionary<string, object>[]>("Extensibility", "Implement an TemplatorKeyWord and use AddKeyword method to add it to config object before passing it to the parser(or if after, call PrepareKeywords() to refresh the keywords), See below for the options:", GetKeywordsProperties().ToArray()),
            new Triple<string, string, IDictionary<string, object>[]>("Configuration", "Templator allows to be fully customized through config object, see the following options for details:", GetConfiguableProperties().ToArray()),
        };

        private static IDictionary<string, object>[] GetBuildTaskConfigurations()
        {
            return new IDictionary<string, object>[]
            {
                new Dictionary<string, object>(){{"Name", "Path"}, {"Description", "The path which the task will only look into, default 'Templates'"}}, 
                new Dictionary<string, object>(){{"Name", "Filters"}, {"Description", "The file extension filters, default '.xml,.csv,.txt' "}}, 
                new Dictionary<string, object>(){{"Name", "Depth"}, {"Description", "The the depth inside the directory the task will look into, default 3"}}, 
            };
        }

        public static IDictionary<string, object> GetInputDict()
        {
            var ret = new Dictionary<string, object>
            {
                {"Title", Title},
                {"Description", Description},
                {"Sections", Sections.Select(s => new Dictionary<string, object>{{"Name", s.First}, {"Description", s.Second}, {"Details", s.Third}}).ToArray()},
                {
                    "Keywords", Config.Keywords.Values.Where(k => k.Description != null).Select(k => new Dictionary<string, object>()
                    {
                        {"Name", k.Name},
                        {"Description", k.Description},
                        {"Params", k.Params.IsNullOrEmpty() ? null : k.Params.Select(p => new Dictionary<string, object>
                            {
                                {"Description",p.First},
                                {"Comments",p.Second},
                            }).ToArray()}
                    }).ToArray()
                }
            };
            return ret;
        }

        private static IEnumerable<IDictionary<string, object>> GetSyntaxExamples()
        {
            yield return new Dictionary<string, object> {{"Name", "Basic"}, {"Description", "{{HolderName}}"}};
            yield return new Dictionary<string, object> {{"Name", "Categorized"}, {"Description", "{{Category(HolderName)}}"}};
            yield return new Dictionary<string, object> {{"Name", "With Parameter"}, {"Description", "{{HolderName[Number(#.##)]}},{{Category(HolderName)[Number(#.##)]}}, see keywords document for details"}};
            yield return new Dictionary<string, object> { { "Name", "Nested holders" }, { "Description", "Nested holders is to make a block which wraps other TextHolers so that it can be controlled by If conditions" } };
            yield return new Dictionary<string, object> { { "Name", "Nested Text" }, { "Description", "{{Holder[]FreeTextAfterHolderInput}} or {{(Holder)FreeTextBeforeHolderItSelf[If]}}" } };
            yield return new Dictionary<string, object> { { "Name", "Nested holders, nested value comes before" }, { "Description", "{{(Holder){{AnotherOne}}[]}} or {{(Holder){{AnotherOne}}}}" } };
            yield return new Dictionary<string, object> { { "Name", "Nested holders, nested value comes After" }, { "Description", "{{(Holder)[]{{AnotherOne}}}} or {{(Holder)[If(ConditionHolder)]{{AnotherOne}}}}" } };

        }
        private static IEnumerable<IDictionary<string, object>> GetKeywordsProperties()
        {
            return (from p in typeof(TemplatorKeyword).GetFields(BindingFlags.Public | BindingFlags.Instance) let d = p.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>() where d != null select new Dictionary<string, object> { { "Name", p.Name }, { "Description", d.Description } }).Cast<IDictionary<string, object>>();
        }
        private static IEnumerable<IDictionary<string, object>> GetConfiguableProperties()
        {
            return (from p in typeof(TemplatorConfig).GetFields(BindingFlags.Public | BindingFlags.Instance) let d = p.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>() where d != null select new Dictionary<string, object>{{"Name", p.Name}, {"Description", d.Description}}).Cast<IDictionary<string, object>>();
        }
    }
}
