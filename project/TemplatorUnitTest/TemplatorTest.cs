﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using DotNetUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Templator;

namespace TemplatorUnitTest
{
    [TestClass]
    public class TemplatorTest
    {
        private readonly TemplatorLogger _logs = new TemplatorLogger();
        private readonly TemplatorConfig _config = new TemplatorConfig();
        [TestMethod]
        public void SimpleDataTest()
        {
            //TTODO: test repeat within multiple deep collection merge e.g. c{d(e)}, c{d(g)} => c{d(e,g)}
            _config.Logger = _logs;
            _config.EscapePrefix = "\\";
            var parser = new TemplatorParser(_config);
            foreach (var entry in GetSimpleDataEntries())
            {
                parser.StartOver();
                _logs.Errors.Clear();
                if (entry.IsXml)
                {
                    parser.ParseXml(entry.Xml, entry.Input);
                }
                else if(entry.IsCsv)
                {
                    parser.ParseCsv(entry.Template, entry.Input);
                }
                else
                {
                    parser.ParseText(entry.Template, entry.Input);
                }
                Assert.AreEqual(entry.FieldCount, parser.Holders.Count);
                if (_logs.Errors.Count > 0)
                {
                    var errors = _logs.Errors.Select(i => i.Message).Join("$$");
                    Assert.AreEqual(entry.Log, errors);
                }
                else
                {
                    Assert.IsTrue(entry.Log.IsNullOrEmpty());
                    if (entry.IsXml)
                    {
                        Assert.IsTrue(XNode.DeepEquals(entry.XmlOutput, parser.XmlContext.Element));
                    }
                    else
                    {
                        Assert.AreEqual(entry.Output, parser.Context.Result.ToString());
                    }
                }
                if (!entry.Levels.IsNullOrEmpty())
                {
                    var fields = parser.Holders;
                    foreach (var s in entry.Levels.Split(','))
                    {
                        Assert.IsTrue(fields.ContainsKey(s));
                        fields = fields[s].Children;
                    }
                }
            }
        }

        private IEnumerable<SimpleDataEntry> GetSimpleDataEntries()
        {
            var id = 0;
            foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (name.StartsWith("TemplatorUnitTest.SimpleData."))
                {
                    using (var rd = new StreamReader(name.GetResourceStreamFromExecutingAssembly()))
                    {
                        var line = rd.ReadLine();
                        while (line != null)
                        {
                            yield return new SimpleDataEntry(name, id++)
                            {
                                IsXml = line == "xml",
                                IsCsv = line == "csv",
                                Template = line == "xml" || line == "csv" ? rd.ReadLine() : line,
                                Input = rd.ReadLine().ParseJsonDict(),
                                Output = rd.ReadLine(),
                                FieldCount = int.Parse(rd.ReadLine()),
                                Levels = rd.ReadLine(),
                                Log = rd.ReadLine(),
                            };
                            line = rd.ReadLine();
                        }
                    }
                }
            }
        } 

        private class SimpleDataEntry
        {
            private readonly string _fileName;
            public readonly int Id;
            public bool IsXml;
            public bool IsCsv;
            public string Log;
            public string Template;
            public IDictionary<string, object> Input;
            public string Output;
            public int FieldCount;
            public string Levels;

            public SimpleDataEntry(string fileName, int id)
            {
                _fileName = fileName;
                Id = id;
            }

            public XElement Xml
            {
                get { return IsXml ? XElement.Parse(Template) : null; }
            }
            public XElement XmlOutput
            {
                get { return IsXml ? Output.IsNullOrEmpty() ? null : XElement.Parse(Output) : null; }
            }

            public override string ToString()
            {
                return Id.ToString();
            }
        }
    }
}
