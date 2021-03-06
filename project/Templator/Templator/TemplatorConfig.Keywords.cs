﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using System.Xml.XPath;
using CsvEnumerator;
using DotNetUtils;

namespace Templator
{
    public partial class TemplatorConfig
    {
        private IList<TemplatorKeyword> _customKeywords;

        public void AddKeyword(TemplatorKeyword newKeyword)
        {
            if (newKeyword == null)
            {
                return;
            }
            if (_customKeywords == null)
            {
                _customKeywords = new List<TemplatorKeyword>();
            }
            _customKeywords.Add(newKeyword);
        }

        public void PrepareKeywords()
        {
            Keywords = (new List<TemplatorKeyword>()
            {
                //Structure keywords
                new TemplatorKeyword(KeywordRepeat){
                    HandleNullOrEmpty = true,
                    Description = "Indicates an Array/Collection/Repeat, corresponding input should be an array : IDictionary<string,object>[], templator will repeat the template starting from this Holder till the matching '{0}'(in plain text) or the close tag of the element(in xml).".FormatInvariantCulture(KeywordRepeatEnd),
                    Examples = new List<Triple<string, string, string>>
                    {
                        new Triple<string, string, string>("{{HolderName[Collection]}}{{RepeatedHolder}}{{HolderName[CollectionEnd]}}","{HolderName: [{RepeatedHolder:1},{RepeatedHolder:2}]}","12"),
                        new Triple<string, string, string>("<xml><r Bindings=\"{{HolderName[Collection]}}\">Repeated</r></xml>","{HolderName: [{},{}]}","<xml><r>Repeated</r><r>Repeated</r></xml>"),
                    },
                    OnGetValue = (holder, parser, value) => value == null ? null : parser.InXmlManipulation() ? value : String.Empty, 
                    PostParse = (parser, parsedHolder) =>
                    {
                        Keywords[KeywordRepeatBegin].PostParse(parser, parsedHolder);
                        if (parser.NoInput)
                        {
                            return false;
                        }
                        if (parser.InXmlManipulation())
                        {
                            Keywords[KeywordRepeatEnd].PostParse(parser, parsedHolder);
                        }
                        return false;
                    } 
                },
                new TemplatorKeyword(KeywordRepeatBegin){
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) => value == null ? null : parser.InXmlManipulation() ? value : String.Empty, 
                    Description = "Indicates beginning position of an Array/Repeat/Collection (or beginning xml element in xml), must match with '{0}', it functions the same as {1} in plain text format".FormatInvariantCulture(KeywordRepeatEnd, KeywordRepeat),
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("Group", "To control the repeat behavior of repeating a group of XElement, e.g: double '<a/><b/>' -> '<a/><b/><a/><b/>' instead of '<a/><a/><b/><b/>'")
                    },
                    PostParse = (parser, parsedHolder) =>
                    {
                        parser.Context.Holders.AddOrSkip(parsedHolder.Name, parsedHolder);
                        if (parser.NoInput)
                        {
                            parser.PushContext(null, null, parsedHolder, false, false);
                            return false;
                        }
                        var childInputs = TemplatorUtil.GetChildCollection(parser.Context.Input, parsedHolder.Name, parser.Config);
                        IDictionary<string, object> input = null;
                        var l = parser.StackLevel + parsedHolder.Name;
                        var inputIndex = TemplatorUtil.GetInputIndex(parser, parsedHolder) ?? 0;
                        var inputCount = 0;
                        var noOutput = false;
                        if (childInputs.IsNullOrEmpty())
                        {
                            noOutput = parsedHolder.IsOptional();
                            TemplatorUtil.SetInputCount(parser, parsedHolder, 0);
                            TemplatorUtil.SetInputIndex(parser, parsedHolder, inputIndex);
                            input = parser.Context.Input == null ? null : new Dictionary<string, object>() { { ReservedKeywordParent, parser.Context.Input } };
                        }
                        else
                        {
                            if (inputIndex < childInputs.Length)
                            {
                                input = childInputs[inputIndex++];
                            }
                            else
                            {
                                noOutput = true;
                                input = new Dictionary<string, object>() { { ReservedKeywordParent, parser.Context.Input } };
                            }
                            TemplatorUtil.SetInputIndex(parser, parsedHolder, inputIndex);
                            if (TemplatorUtil.GetInputCount(parser, parsedHolder) == null || !(parsedHolder.ContainsKey(KeywordLength) || parsedHolder.ContainsKey(KeywordAlignCount)))
                            {
                                inputCount = childInputs.Length;
                                TemplatorUtil.SetInputCount(parser, parsedHolder, inputCount);
                            }
                            else
                            {
                                inputCount = (int) TemplatorUtil.GetInputCount(parser, parsedHolder);
                            }
                        }
                        if (parser.InXmlManipulation())
                        {
                            parser.ParentXmlContext.OnAfterParsingElement = null;
                            if (inputIndex < inputCount )
                            {
                                parser.ParentXmlContext[l + "XmlElementIndex"] = parser.ParentXmlContext.ElementIndex;
                                parser.ParentXmlContext.OnBeforeParsingElement = p =>
                                {
                                    var element = new XElement((XElement)p.XmlContext.ElementList[p.XmlContext.ElementIndex]);
                                    if ((string)parsedHolder[KeywordRepeatBegin] == "Group")
                                    {
                                        parser.XmlContext.Element.Add(element);
                                    }
                                    else
                                    {
                                        p.XmlContext.ElementList[p.XmlContext.ElementIndex].AddAfterSelf(element);
                                    }
                                    p.XmlContext.ElementList[p.XmlContext.ElementIndex] = element;
                                };
                                var newElement = new XElement((XElement)parser.ParentXmlContext.ElementList[parser.ParentXmlContext.ElementIndex]);
                                if ((string)parsedHolder[KeywordRepeatBegin] == "Group")
                                {
                                    parser.ParentXmlContext.Element.Add(newElement);
                                }
                                else
                                {
                                    ((XElement)parser.ParentXmlContext.ElementList[parser.ParentXmlContext.ElementIndex]).InsertElementAfter(newElement);
                                }
                                parser.ParentXmlContext.ElementList[parser.ParentXmlContext.ElementIndex] = newElement;
                            }
                        }

                        parser.PushContext(input, null, parsedHolder, parsedHolder.ContainsKey(KeywordHolder) || inputCount == 0, noOutput);
                        parser.Context["ParentPosition"] = parsedHolder.Position;
                        return false;
                    } 
                },
                new TemplatorKeyword(KeywordRepeatEnd){
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    Description = "Indicates then end position (end element in xml) of Array/Collection/Repeat",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("Group", "To control the repeat behavior of repeating a group of XElement, e.g: double '<a/><b/>' with <c/> -> '<a/><b/><a/><b/><c/>' instead of '<a/><b/><c/><a/><b/>'")
                    },
                    OnGetValue = (holder, parser, value) => String.Empty,
                    PostParse = (parser, parsedHolder) =>
                    {
                        
                        if (parser.InXmlManipulation())
                        {
                            parser.ParentXmlContext.OnAfterParsingElement = p =>
                            {
                                p.PopContext();
                                if (parser.NoInput)
                                {
                                    return;
                                }
                                var ll = p.StackLevel + parsedHolder.Name;
                                if (TemplatorUtil.GetInputCount(p, parsedHolder) > TemplatorUtil.GetInputIndex(p, parsedHolder))
                                {
                                    p.XmlContext.ElementIndex = (int)p.XmlContext[ll + "XmlElementIndex"] - 1;
                                }
                                else
                                {
                                    TemplatorUtil.SetInputIndex(p, parsedHolder, null);
                                    p.XmlContext.OnBeforeParsingElement = null;
                                    if ((string)parsedHolder[KeywordRepeatEnd] == "Group")
                                    {
                                        for (var i = p.XmlContext.ElementIndex+1; i < p.XmlContext.ElementList.Count; i++)
                                        {
                                            ((XElement)p.XmlContext.ElementList[i]).MoveLast();
                                        }
                                    }
                                }
                                p.XmlContext.OnAfterParsingElement = null;
                            };
                            parser.ParentXmlContext.OnBeforeParsingElement = null;
                        }
                        else
                        {
                            if (parser.NoInput)
                            {
                                parser.PopContext();
                                return false;
                            }
                            var position = (int)parser.Context["ParentPosition"];
                            parser.PopContext();
                            if (TemplatorUtil.GetInputIndex(parser, parsedHolder) == null)
                            {
                                parser.LogSyntaxError("Probably the CollectionEnd holder didn't use the same name with the beginning Holder, end holder name : '{0}'".FormatInvariantCulture(parsedHolder.Name));
                                parser.Context.State.Error = true;
                                return false;
                            }
                            if (TemplatorUtil.GetInputCount(parser, parsedHolder) > TemplatorUtil.GetInputIndex(parser, parsedHolder))
                            {
                                parser.Context.Text.Position = position;
                            }
                            else
                            {
                                TemplatorUtil.SetInputIndex(parser, parsedHolder, null);
                            }
                        }
                        return false;
                    }
                },
                new TemplatorKeyword(KeywordNested)
                {
                    Description = "If a TextHolder's input value contains Templator syntax, mark the holder with this Keyword to enable the template in the value string to be processed with the same context",
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (value != null)
                        {
                            parser.PushContext(parser.Context.Input, new SeekableString((string)value), holder, true);
                            parser.ParseTextInternal();
                            parser.PopContext();
                            return parser.Context.Result.ToString();
                        }
                        return null;
                    } 
                },
                new TemplatorKeyword(KeywordNestedXml)
                {
                    Description = "Allow a xml Template's input value provided for a TextHolder marked with this keyword to be processed",
                    PostParse = (parser, holder) => parser.Context.Input != null,
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (value != null)
                        {
                            var e = XElement.Parse((string) value);
                            parser.ParseXmlInternal(e);
                            parser.XmlContext.Element.Add(e);
                            return String.Empty;
                        }
                        return null;
                    } 
                },
                //Calculated Keywords
                new TemplatorKeyword(KeywordSeekup)
                {
                    CalculateInput = true,
                    HandleNullOrEmpty = true,
                    Description = "Allow the parser to seek upper level of the input when current context is inside a child array/repeat/collection loop",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("Number value", "Indicates the number of levels that allowed to seek upwards")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (value == null)
                        {
                            var level = holder[KeywordSeekup].ParseDecimalNullable() ?? 0;
                            var input = parser.Context.Input;
                            while (level-- > 0 && input != null)
                            {
                                if (!input.ContainsKey(ReservedKeywordParent))
                                {
                                    break;
                                }
                                input = (IDictionary<string, object>) input[ReservedKeywordParent];
                                if (input.ContainsKey(holder.Name))
                                {
                                    return input[holder.Name];
                                }
                            }
                        }
                        return value;
                    }, 
                    Parse = ((parser, str) =>
                    {
                        parser.Context.Holder[KeywordSeekup] = str.ParseIntParam(1);
                    })
                },
                new TemplatorKeyword(KeywordJs)
                {
                    HandleNullOrEmpty = true,
                    CalculateInput = true,
                    //Description = "Evaluate a piece of JS code based on current input",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The js code", "")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyword(KeywordMathMax)
                {
                    HandleNullOrEmpty = true,
                    CalculateInput = true,
                    Description = "Aggregate the max value of given Holder names",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("HolderNames separated by ';', or hierarchy separated by '.'", "Something like: 'Holder1;Holder2;Collection.ChildName'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string)holder[KeywordMathMax];
                        return parser.Aggregate(null, holder, aggregateField, parser.Context.Input, (agg, num) => Math.Max((num.ParseDecimalNullable() ?? 0) , (agg.ParseDecimalNullable() ?? 0))).DecimalToString();
                    }
                },
                new TemplatorKeyword(KeywordMathMin)
                {
                    HandleNullOrEmpty = true,
                    CalculateInput = true,
                    Description = "Aggregate the min value of given Holder names",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("HolderNames separated by ';', or hierarchy separated by '.'", "Something like: 'Holder1;Holder2;Collection.ChildName'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string)holder[KeywordMathMin];
                        return parser.Aggregate(null, holder, aggregateField, parser.Context.Input, (agg, num) => Math.Min((num.ParseDecimalNullable() ?? 0) , (agg.ParseDecimalNullable() ?? 0))).DecimalToString();
                    }
                },
                new TemplatorKeyword(KeywordSum)
                {
                    HandleNullOrEmpty = true,
                    CalculateInput = true,
                    Description = "Aggregate the sum value of given Holder names",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("HolderNames separated by ';', or hierarchy separated by '.'", "Something like: 'Holder1;Holder2;Collection.ChildName'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string)holder[KeywordSum];
                        return parser.Aggregate(null, holder, aggregateField, parser.Context.Input, (agg, num) => (num.ParseDecimalNullable() ?? 0) + (agg.ParseDecimalNullable() ?? 0)).DecimalToString();
                    }
                },
                new TemplatorKeyword(KeywordAverage)
                {
                    HandleNullOrEmpty = true,
                    CalculateInput = true,
                    Description = "Aggregate the average value of given Holder names",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("HolderNames separated by ';', or hierarchy separated by '.'", "Something like: 'Holder1;Holder2;Collection.ChildName'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string)holder[KeywordAverage];
                        // sum/count
                        var count = (decimal)parser.Aggregate(null, holder, aggregateField, parser.Context.Input, (agg, item) => (agg.ParseDecimalNullable() ?? 0) + ((item as object[])?.Length ?? 1));
                        return count != 0 ? (decimal)parser.Aggregate(null, holder, aggregateField, parser.Context.Input, (agg, num) => (num.ParseDecimalNullable() ?? 0) + (agg.ParseDecimalNullable() ?? 0)) / count : 0;
                    }
                },
                new TemplatorKeyword(KeywordCount)
                {
                    HandleNullOrEmpty = true,
                    CalculateInput = true,
                    Description = "Aggregate the count of given Holder which is an Array/Repeat/Collection ",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("HolderNames separated by ';', or hierarchy separated by '.'", "Something like: 'CollectionName1;CollectionName2.ChildCollectionName'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string) holder[KeywordCount];
                        return parser.Aggregate(null, holder, aggregateField, parser.Context.Input, (agg, item) => (agg.ParseDecimalNullable() ?? 0) + ((item as object[])?.Length ?? 1)).DecimalToString();
                    }
                },
                new TemplatorKeyword(KeywordMulti)
                {
                    HandleNullOrEmpty = true,
                    CalculateInput = true,
                    Description = "Aggregate the multiplied value of given Holder names",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("HolderNames separated by ';', or hierarchy separated by '.'", "Something like: 'Holder1;Holder2;Collection.ChildName'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string)holder[KeywordMulti];
                        return parser.Aggregate(null, holder, aggregateField, parser.Context.Input, (agg, num) => (num.ParseDecimalNullable() ?? 1) * (agg.ParseDecimalNullable() ?? 1)).DecimalToString();
                    }
                },
                //Modifying Input
                new TemplatorKeyword(KeywordRefer)
                {
                    HandleNullOrEmpty = true,
                    ManipulateInput = true,
                    Description = "Retrieve another 'referred' TextHolder's value as the value",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The referred Holder's Name", "")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var refer = (string)holder[KeywordRefer];
                        return TemplatorUtil.GetValue(parser, refer, parser.Context.Input, null, (int?)holder[KeywordSeekup] ?? 0);
                    },
                    Parse = (parser, str) =>
                    {
                        if (str.IsNullOrWhiteSpace())
                        {
                            parser.Context.State.Error = true;
                            if (parser.Config.IgnoreUnknownParam || parser.Config.ContinueOnError)
                            {
                                return;
                            }
                            throw new TemplatorParamsException();
                        }
                        parser.Context.Holder[KeywordRefer] = str;
                    }
                },
                new TemplatorKeyword(KeywordEven)
                {
                    ManipulateInput = true,
                    Description = "Specify the method of rounding a decimal as 'Even', only works with keyword '{0}'".FormatInvariantCulture(KeywordNumber),
                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDecimalNullable();
                        return d.HasValue ? (object) decimal.Round(d.Value, MidpointRounding.ToEven) : null;
                    }
                },
                new TemplatorKeyword(KeywordAwayFromZero)
                {
                    ManipulateInput = true,
                    Description = "Specify the method of rounding a decimal as 'AwayFromZero', only works with keyword '{0}'".FormatInvariantCulture(KeywordNumber),
                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDecimalNullable();
                        return d.HasValue ? (object) decimal.Round(d.Value, (int?)holder[KeywordAwayFromZero].ParseDecimalNullable() ?? 2, MidpointRounding.AwayFromZero) : null;
                    }
                },
                new TemplatorKeyword(KeywordDefault)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    ManipulateInput = true,
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The default value, none as String.Empty", "")
                    },
                    Description = "Provide a default INPUT value of the TextHolder if no value found from input",
                    OnGetValue = (holder, parser, value) => value ?? holder[KeywordDefault],
                },
                new TemplatorKeyword(KeywordOptional)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    ManipulateOutput = true,
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The default value, none as String.Empty", "")
                    },
                    Description = "Provide a default OUTPUT value of the TextHolder if no value found from input",
                    OnGetValue = (holder, parser, value) => value ?? holder[KeywordOptional],
                },
                //Validation Keywords
                new TemplatorKeyword(KeywordRegex)
                {
                    IsValidation = true,
                    Description = "Validate the input with specified regular expression, this keyword will try to find a preset regex in config using the given string as key. if not found will use the given string as the expression",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The name/key of the expressions in config object, or the regular expression string", "")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var regStr = (string) holder[KeywordRegex];
                        if (!regStr.IsNullOrWhiteSpace())
                        {
                            var d = Regexes.ContainsKey(regStr);
                            var reg = d ? Regexes[regStr] : new Regex(regStr, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                            if (!reg.IsMatch(value.SafeToString()))
                            {
                                parser.LogError("Value '{0}' test failed against pattern '{1}' for field: '{2}'", value, d ? Regexes[regStr].ToString() : regStr, holder.Name);
                                return null;
                            }
                        }
                        else
                        {
                            throw new TemplatorParamsException("Invalid Regular expression defined");
                        }
                        return value;
                    },
                    Parse = (parser, str) =>
                    {
                        parser.Context.Holder[KeywordRegex] = str;
                    }
                },
                new TemplatorKeyword(KeywordLength)
                {
                    IsValidation = true,
                    HandleNullOrEmpty = true,
                    Description = "Validate input strings' length, or fix the output length if current TextHolder indicates an Array/Repeat/Collection",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The valid length values", "E.g.: '3', '1;3;5', '1-7'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var isArray = holder.ContainsKey(KeywordRepeat) || holder.ContainsKey(KeywordRepeatBegin);
                        if (value.IsNullOrEmptyValue() && ! isArray)
                        {
                            return value;
                        }
                        var str = holder.ContainsKey(KeywordNumber) ? Convert.ToString(value.DecimalToString() ?? value) : value.SafeToString();
                        var child = isArray ? TemplatorUtil.GetChildCollection(parser.Context.Input, holder.Name, parser.Config) : null;
                        var length = isArray ? child?.Length ?? 0 : str.Length;
                        int? maxLength = null;
                        var customLength = (Pair<string, IList<int>>)holder[KeywordLength];
                        if (customLength.Second[0] == -1)
                        {
                            maxLength = customLength.Second[2];
                            if (length < customLength.Second[1] || length > customLength.Second[2])
                            {
                                goto invalidLength;
                            }
                        }
                        else if (!customLength.Second.Contains(length))
                        {
                            if (customLength.Second.Count == 1)
                            {
                                maxLength = customLength.Second[0];
                            }
                            goto invalidLength;
                        }
                        return str;
                        invalidLength:
                        if (maxLength.HasValue && holder.ContainsKey(KeywordTruncate))
                        {
                            if (isArray && child != null)
                            {
                                TemplatorUtil.SetInputCount(parser, holder, maxLength.Value);
                            }
                            else
                            {
                                str = str.Substring(0, maxLength.Value);
                            }
                            return str;
                        }
                        parser.LogError("Invalid Field length: '{0}', value: {1}, valid length: {2}.", holder.Name, value, customLength.First);
                        return null;
                    },
                    Parse = ((parser, str) =>
                    {
                        IList<int> lengths;
                        if (str.Contains("-"))
                        {
                            lengths = str.Split('-').Select(s => s.ParseIntNullable()).Where(i => i.HasValue && i > 0).Select(i => i.Value).ToList();
                            if (lengths.Count != 2)
                            {
                                parser.Context.State.Error = true;
                                if (parser.Config.IgnoreUnknownParam || parser.Config.ContinueOnError)
                                {
                                    return;
                                }
                                throw new TemplatorParamsException("Invalid length defined");
                            }
                            lengths.Insert(0, -1);
                        }
                        else if(str.Contains(";"))
                        {
                            lengths = str.Split(';').Select(s => s.ParseIntNullable()).Where(i => i.HasValue && i > 0).Select(i => i.Value).ToList();
                            if (lengths.Count == 0)
                            {
                                parser.Context.State.Error = true;
                                if (parser.Config.IgnoreUnknownParam || parser.Config.ContinueOnError)
                                {
                                    return;
                                }
                                throw new TemplatorParamsException("Invalid length defined");
                            }
                        }
                        else
                        {
                            var l = str.ParseIntNullable();
                            if (!l.HasValue)
                            {
                                parser.Context.State.Error = true;
                                if (parser.Config.IgnoreUnknownParam || parser.Config.ContinueOnError)
                                {
                                    return;
                                }
                                throw new TemplatorParamsException("Invalid length defined");
                            }
                            lengths = l.Value.Single().ToList();
                        }
                        parser.Context.Holder[KeywordLength] = new Pair<string,IList<int>>(str, lengths);
                    })
                },
                //new TemplatorKeyword(KeywordExpression){},
                new TemplatorKeyword(KeywordMin)
                {
                    IsValidation = true,
                    Description = "Validate the min value of the value when it is a Number",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The numeric value", "")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var min = (decimal)holder[KeywordMin];
                        if (holder.ContainsKey(KeywordNumber))
                        {
                            var num = value.ParseDecimalNullable();
                            if (num < min)
                            {
                                parser.LogError("'{0}' is not valid for min: '{1}' in field: '{2}'", value, min, holder.Name);
                                return null;
                            }
                            return value;
                        }
                        throw new TemplatorParamsException();
                    },
                    Parse = ((parser, str) =>
                    {
                        parser.Context.Holder[KeywordMin] = str.ParseNumberParam();
                    })
                },
                new TemplatorKeyword(KeywordMax)
                {
                    IsValidation = true,
                    Description = "Validate the max value of the value when it is a Number",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The numeric value", "")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var max = (decimal) holder[KeywordMax];
                        if (holder.ContainsKey(KeywordNumber))
                        {
                            var num = value.ParseDecimalNullable();
                            if (num > max)
                            {
                                parser.LogError("'{0}' is not valid for max: '{1}' in field: '{2}'", value, max, holder.Name);
                                return null;
                            }
                            return value;
                        }
                        throw new TemplatorParamsException();
                    },
                    Parse = ((parser, str) =>
                    {
                        parser.Context.Holder[KeywordMax] = str.ParseNumberParam();
                    })
                },
                //DataType/format Keywords
                new TemplatorKeyword(KeywordBit)
                {
                    IsValidation = true,
                    Description = "Indicates the value is functioning as 'Bit', referred by '{0}', has value means 'true', null indicates 'false'".FormatInvariantCulture(KeywordIf),
                    OnGetValue = (holder, parser, value) => value == null ? null : String.Empty
                },
                new TemplatorKeyword(KeywordEnum){
                    IsValidation = true,
                    Description = "Indicates the value of this holder is an Enum listed in config object.Enums, with the Enum name as key",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The Enum type name as key in the config object", "")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var typeName = (string)holder[KeywordEnum];
                        if (Enum.IsDefined(Enums[typeName], value ?? String.Empty))
                        {
                            return value;
                        }
                        parser.LogError("'{0}' is not a value of enum '{1}' in '{2}'", value, typeName, holder.Name);
                        return null;
                    },
                    Parse =  ((parser, str) =>
                    {
                        if (!str.IsNullOrWhiteSpace() && Enums.ContainsKey(str))
                        {
                            parser.Context.Holder[KeywordEnum] = str;
                        }
                        else if(!parser.Config.IgnoreUnknownParam && !parser.Config.ContinueOnError)
                        {
                            throw new TemplatorParamsException();
                        }
                    })
                },
                new TemplatorKeyword(KeywordNumber)
                {
                    IsValidation = true,
                    ManipulateOutput = true,
                    Description = "Indicates and validate that the input value is a Number",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("None or the output format of the number", "E.g.: Number(0.00) with value 0.1 -> 0.10")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDecimalNullable();
                        if (d == null)
                        {
                            parser.LogError("'{0}' is not a number in '{1}'", value, holder.Name);
                            return null;
                        }
                        var format = (string)holder[KeywordNumber];
                        return d.Value.ToString(format.IsNullOrWhiteSpace() ? "G29" : format);
                    }
                },
                new TemplatorKeyword(KeywordDateTime)
                {
                    IsValidation = true,
                    ManipulateOutput = true,
                    Description = "Indicates and validate that the input value is a DateTime, parsing with the 'DateFormat' in config",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("None or the format of the output of the DateTime value", "E.g.: DateTime(yyyy) with value 07/07/2015 -> 2015")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDateTimeNullable();
                        if (d == null)
                        {
                            parser.LogError("'{0}' is not a valid DateTime in '{1}'", value, holder.Name);
                            return null;
                        }
                        var format = (string)holder[KeywordDateTime];
                        if (format.IsNullOrWhiteSpace())
                        {
                            return d;
                        }
                        return d.Value.ToString(format);
                    }
                },
                //Format Keywords
                new TemplatorKeyword(KeywordFormat)
                {
                    ManipulateOutput = true,
                    Description = "Use String.Format to put the value into param string's '{0}' position",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The string pattern", "E.g.: 'hello {0}' with value 'world' -> 'hello world'")
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (null == value)
                        {
                            return null;
                        }
                        var pattern = (string)holder[KeywordFormat];
                        return pattern.Format(value);
                    }
                },
                new TemplatorKeyword(KeywordMap)
                {
                    ManipulateOutput = true,
                    Description = "Map/replace output with the pair provided in the param to transform input",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("Pairs(separated by ';') of values(separated by ':') to map with input value", "E.g.: a:1;b:2 with value a -> 1 and b -> 2" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var dict = (IDictionary<string, string>)holder[KeywordMap];
                        var str = value.SafeToString();
                        if (dict.ContainsKey(str))
                        {
                            return dict[str];
                        }
                        parser.LogError("'{0}' unexpected input value of field '{1}'", value, holder.Name);
                        return null;
                    },
                    Parse = (parser, str) =>
                    {
                        var ret = new Dictionary<string, string>();
                        foreach (var item in str.Split(Constants.SemiDelimChar))
                        {
                            string value;
                            var key = item.GetUntil(":", out value);
                            ret.Add(key.Trim(), value.Trim());
                        }
                        parser.Context.Holder[KeywordMap] = ret;
                    }
                },
                new TemplatorKeyword(KeywordReplace)
                {
                    ManipulateOutput = true,
                    Description = "Replace specific string with another value in the output",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("Pair of values(separated by ';')", "E.g.: a;1 with value abc -> 1bc" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (value != null)
                        {
                            var arr = (string[])holder[KeywordReplace];
                            return value.SafeToString().Replace(arr[0], arr[1]);
                        }
                        return null;
                    },
                    Parse = (parser, str) =>
                    {
                        var arr = str.Split(';');
                        if (arr.Length == 2)
                        {
                            parser.Context.Holder[KeywordReplace] = arr;
                            return;
                        }
                        parser.Context.State.Error = true;
                        if (parser.Config.IgnoreUnknownParam || parser.Config.ContinueOnError)
                        {
                            return;
                        }
                        throw new TemplatorParamsException();
                    }
                },
                new TemplatorKeyword(KeywordTransform)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output based on parameter options",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("Lower/Upper", "Support only to upper or to lower for now" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var str = value.SafeToString();
                        switch ((string)holder[KeywordTransform])
                        {
                            case "Lower":
                                return str.ToLower();
                            case "Upper":
                                return str.ToUpper();
                        }
                        return str;
                    }
                },
                new TemplatorKeyword(KeywordUpper)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to upper case",
                    OnGetValue = (holder, parser, value) =>
                    {
                        value = value.SafeToString().ToUpper();
                        return value;
                    }
                },
                new TemplatorKeyword(KeywordLower)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to lower case",
                    OnGetValue = (holder, parser, value) =>
                    {
                        value = value.SafeToString().ToLower();
                        return value;
                    }
                },
                new TemplatorKeyword(KeywordTrim)
                {
                    ManipulateOutput = true,
                    Description = "Trim the output string",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("'Begin'/'End' or none", "Trim Begin or End or no param for both" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var str = value.SafeToString();
                        var trim = (string)holder[KeywordTrim];
                        switch (trim)
                        {
                            case null:
                                return str;
                            case "Begin":
                                return str.TrimStart();
                            case "End":
                                return str.TrimEnd();
                            default:
                                return str.Trim();
                        }
                    }
                },
                new TemplatorKeyword(KeywordCsv)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to encode for used as a csv cell",
                    OnGetValue = (holder, parser, value) => value.SafeToString().EncodeCsvField()
                },
                new TemplatorKeyword(KeywordBase32)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to encode with base32",
                    OnGetValue = (holder, parser, value) => Base32.ToBase32String(parser.Config.Encoding.GetBytes(value.SafeToString()))
                },
                new TemplatorKeyword(KeywordBase64)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to encode with base64",
                    OnGetValue = (holder, parser, value) => Convert.ToBase64String(parser.Config.Encoding.GetBytes(value.SafeToString()))
                },
                new TemplatorKeyword(KeywordUrl)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to encode with url encode",
                    OnGetValue = (holder, parser, value) => HttpUtility.UrlEncode(value.SafeToString())
                },
                new TemplatorKeyword(KeywordHtml)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to encode with html encode",
                    OnGetValue = (holder, parser, value) => HttpUtility.HtmlEncode(value.SafeToString())
                },
                new TemplatorKeyword(KeywordEncode)
                {
                    ManipulateOutput = true,
                    Description = "Transform the output string to encode with the options in Parameter",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The encode method ", "Base32/Base64/Html/Csv/Url" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        switch ((string)holder[KeywordEncode])
                        {
                            case "Base64":
                                return Base32.ToBase32String(parser.Config.Encoding.GetBytes(value.SafeToString()));
                            case "Base32":
                                return Convert.ToBase64String(parser.Config.Encoding.GetBytes(value.SafeToString()));
                            case "Html":
                                return HttpUtility.HtmlEncode(value.SafeToString());
                            case "Url":
                                return HttpUtility.UrlEncode(value.SafeToString());
                            case "Csv":
                                return value.SafeToString().EncodeCsvField();
                            default:
                                throw new TemplatorParamsException();
                        }
                    }
                },
                new TemplatorKeyword(KeywordDecode)
                {
                    ManipulateOutput = true,
                    Description = "Decode the output string with the options in Parameter",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The decode method ", "Base32/Base64/Html/Csv/Url" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        switch ((string)holder[KeywordDecode])
                        {
                            case "Base64":
                                return parser.Config.Encoding.GetString(Base32.FromBase32String(value.SafeToString()));
                            case "Base32":
                                return parser.Config.Encoding.GetString(Convert.FromBase64String(value.SafeToString()));
                            case "Html":
                                return HttpUtility.HtmlDecode(value.SafeToString());
                            case "Url":
                                return HttpUtility.UrlDecode(value.SafeToString());
                            case "Csv":
                                 return new SeekableString(value.SafeToString()).DecodeCsvField(true);
                            default:
                                throw new TemplatorParamsException();
                        }
                    }
                },
                new TemplatorKeyword(KeywordRemoveChar)
                {
                    ManipulateOutput = true,
                    Description = "Remove a specific character from the output",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The char to remove ", "E.g.: RemoveChar(.) with input 1.00 -> 100" )
                    },
                    OnGetValue = (holder, parser, value) => value.SafeToString().RemoveCharacter((char[]) holder[KeywordRemoveChar]),
                    Parse = ((parser, str) =>
                    {
                        if (str.Length != 1)
                        {
                            parser.Context.State.Error = true;
                            if (parser.Config.IgnoreUnknownParam || parser.Config.ContinueOnError)
                            {
                                return;
                            }
                            throw new TemplatorParamsException();
                        }
                        parser.Context.Holder[KeywordRemoveChar] = str.ToCharArray();
                    })
                },
                new TemplatorKeyword(KeywordFixedLength)
                {
                    HandleNullOrEmpty = true,
                    ManipulateOutput = true,
                    Description = "Ensure the output length is fixed by the number specified in Param, truncate if too long or fill with the Char provides by keyword '{0}', default is white space".FormatInvariantCulture(KeywordFill),
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("Length needed", "E.g.: FixedLength(3) with input 'a' -> 'a  '" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (value != null)
                        {
                            var length = (int) holder[KeywordFixedLength];
                            return value.SafeToString()
                                .ToFixLength(length,
                                    (((string) holder[KeywordFill] ??
                                      (string) holder[KeywordFill] ?? (string) holder[KeywordPrefill]).NullIfEmpty() ??
                                     " ").First(),
                                    !holder.ContainsKey(KeywordPrefill));
                        }
                        return null;
                    },
                    Parse = ((parser, str) =>
                    {
                        parser.Context.Holder[KeywordFixedLength] = str.ParseIntParam();
                    })
                },
                new TemplatorKeyword(KeywordSelect)
                {
                    ManipulateOutput = true,
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    Description = "Perform a logic '?:' operator",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("3 Params delimited by ';', 3rd one optional. leave blank means using the field name itself as condition.", "if the condition is true (condition accepts '!' operator), output 1st param, else output the 2nd one" ),
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var p = TemplatorUtil.Get3Params(parser, holder, KeywordSelect, ";");
                        var condition = TemplatorUtil.EvalulateCondition(parser, holder, p.Third, value);
                        return condition ? p.First : p.Second;
                    }
                },
                new TemplatorKeyword(KeywordJoin)
                {
                    ManipulateOutput = true,
                    HandleNullOrEmpty = true,
                    Description = "Apply logic as String.Join to an collection/array, e.g. insert specific string before each item except the first one",
                    
                    OnGetValue = (holder, parser, value) =>
                    {
                        var i = TemplatorUtil.GetInputIndex(parser, holder);
                        if (i.HasValue && i > 0)
                        {
                            return String.Concat(holder[KeywordJoin], value);
                        }
                        return value;
                    }
                },
                new TemplatorKeyword(KeywordWrap)
                {
                    ManipulateOutput = true,
                    HandleNullOrEmpty = true,
                    Description = "Wrap the collection with begin/end tags if the collection (itself or another field name if supplied in the third parameter) is not empty, condition accepts '!' operator",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("If specified at a non-collection item : The begin tag + ';' + end tag + ';' + optional conditional holder name", "E.g.: Wrap([) or Wrap([;]) or Wrap(;]) or Wrap([;];AnotherHolder)" ),
                        new Pair<string, string>("The begin tag at collectionBegin or the end tag at the collectionEnd", "E.g.: Collection,Wrap([) or CollectionEnd,Wrap(])" ),
                        new Pair<string, string>("Optional field Name used to determine if to wrap instead of the collection itself", "E.g.: Collection,Wrap([;AnotherHolder) or CollectionEnd,Wrap(];AnotherHolder)" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        bool isCollection = false, begin = false, end = false;
                        if (holder.ContainsKey(parser.Config.KeywordRepeat) || holder.ContainsKey(parser.Config.KeywordRepeatBegin))
                        {
                            isCollection = true;
                            var i = TemplatorUtil.GetInputIndex(parser, holder);
                            if (!i.HasValue)
                            {
                                begin = true;
                            }
                        }
                        else if(holder.ContainsKey(parser.Config.KeywordRepeatEnd))
                        {
                            isCollection = true;
                            var c = TemplatorUtil.GetParentInputCount(parser, holder);
                            var i = TemplatorUtil.GetParentInputIndex(parser, holder);
                            if (i.HasValue && i == c)
                            {
                                end = true;
                            }
                        }

                        if (!isCollection || begin || end)
                        {
                            var p = TemplatorUtil.Get3Params(parser, holder, KeywordWrap, ";");
                            var name = isCollection ? p.Second : p.Third;
                            if (!isCollection)
                            {
                                var condition = TemplatorUtil.EvalulateCondition(parser, holder, name, value);
                                return condition ? String.Concat(p.First, value, p.Second) : value;
                            }
                            if (name.IsNullOrEmpty())
                            {
                                if (end)
                                {
                                    return p.First;
                                }
                                var collection = TemplatorUtil.GetChildCollection(parser.Context.Input, holder.Name, parser.Config);
                                if (!collection.IsNullOrEmpty())
                                {
                                    return p.First;
                                }
                            }
                            else
                            {
                                var condition = TemplatorUtil.EvalulateCondition(parser, end? parser.ParentContext.Input : parser.Context.Input, holder, name, value);
                                if (condition)
                                {
                                    return p.First;
                                }
                            }
                        }
                        return value;
                    }
                },
                new TemplatorKeyword(KeywordHolder)
                {
                    ManipulateOutput = true,
                    HandleNullOrEmpty = true,
                    Description = "Indicates this TextHolder only required input but will not output anything",
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (parser.XmlContext?.Element != null)
                        {
                            parser.RemovingElements.Add(parser.XmlContext.Element);
                        }
                        return value == null ? null : String.Empty;
                    }
                },
                //document manipulation keywords
                new TemplatorKeyword(KeywordIfnot)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    Description = "If the given value is null or not provided, remove the xml element based on the xpath specified in param, default removing current element",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("An xpath based on current element", "" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var ifQuery = (string)holder[KeywordIfnot];
                        var condition = TemplatorUtil.EvalulateCondition(parser, holder, null, value);
                        if (!condition)
                        {
                            parser.RemovingElements.Add(ifQuery.IsNullOrWhiteSpace()
                            ? parser.XmlContext.Element
                            : parser.XmlContext.Element.XPathSelectElement(ifQuery));
                        }
                        return value ?? String.Empty;
                    }
                },
                new TemplatorKeyword(KeywordEnumElement)
                {
                    Description = "Validate the input with the enum and put the enum value as the name of current xml element",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The Enum type name in the config object","" )
                    },
                    Parse = (parser, str) =>
                    {
                        parser.Context.Holder.Keywords.Add(Keywords[KeywordEnum].Create());
                        parser.Context.Holder[KeywordEnum] = str;
                        parser.Context.Holder.Keywords.Add(Keywords[KeywordElementName].Create());
                    }
                },
                new TemplatorKeyword(KeywordAsXml)
                {
                    Description = "Output the value of this field as an xml Element into the xml template, only working in xml template",
                    Parse = (parser, str) =>
                    {
                        if (parser.InXmlManipulation())
                        {
                            if (parser.XmlContext.Attribute != null)
                            {
                                throw new TemplatorUnexpectedKeywordException("AsXml cannot be placed as xml attribute value");
                            }
                        }
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (!value.IsNullOrEmptyValue())
                        {
                            var element = parser.XmlContext.Element;
                            parser.ParentXmlContext.OnAfterParsingElement = p =>
                            {
                                element.Add(XElement.Parse(value.SafeToString()));
                            };
                        }
                        return String.Empty;
                    }
                },
                new TemplatorKeyword(KeywordElementName)
                {
                    Description = "Use the value as current xml element name instead of value",
                    OnGetValue = (holder, parser, value) =>
                    {
                        parser.XmlContext.Element.Name = parser.XmlContext.Element.Name.Namespace + value.SafeToString();
                        return String.Empty;
                    }
                },
                new TemplatorKeyword(KeywordAttributeThen)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyword(KeywordAttributeIfnot)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyword(KeywordAttributeIf)
                {
                    IndicatesOptional = true,
                    HandleNullOrEmpty = true,
                    Description = "Only keep current xml attribute when value is provided and is not null, the value used is from another TextHolder specified in the param or the current Holder's value if no param specified",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("No Params or another TextHolder's name","Condition accepts '!' operator" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (parser.XmlContext?.Attribute != null)
                        {
                            var condition = TemplatorUtil.EvalulateCondition(parser, holder, (string)holder[KeywordAttributeIf], value);
                            if (!condition)
                            {
                                parser.XmlContext.Attribute.Remove();
                            }
                        }
                        return value ?? string.Empty;
                    }
                },
                new TemplatorKeyword(KeywordAttributeName)
                {
                    ManipulateOutput = true,
                    Description = "Use the value as current attribute's name instead of value",
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (value == null)
                        {
                            parser.LogError("{0} is required", holder.Name);
                            return null;
                        }
                        if (parser.XmlContext?.Attribute != null)
                        {
                            var element = parser.XmlContext.Element;
                            var attr = parser.XmlContext.Attribute;
                            parser.ParentXmlContext.OnAfterParsingElement = templatorParser =>
                            {
                                element.SetAttributeValue(value.SafeToString(), attr.Value);
                                if (attr.Parent != null)
                                {
                                    attr.Remove();
                                }
                            };
                        }
                        return String.Empty;
                    }
                },
                new TemplatorKeyword(KeywordThen)
                {
                    HandleNullOrEmpty = true,
                },
                new TemplatorKeyword(KeywordIf)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    Description = "Only keep current xml attribute when value is provided and is not null, the value used is from another TextHolder specified in the param or the current Holder's value if no param specified",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("No Params or another TextHolder's name","condition accepts '!' operator" )
                    },
                    OnGetValue = (holder, parser, value) =>
                    {
                        var condition = TemplatorUtil.EvalulateCondition(parser, holder, (string)holder[KeywordIf], value);
                        if (!condition)
                        {
                            parser.Context.ChildResultAfter.Clear();
                            parser.Context.ChildResultBefore.Clear();
                            var logger = parser.Context.ChildLogger as TemplatorLogger;
                            logger?.Errors.Clear();
                            if (parser.XmlContext != null)
                            {
                                parser.RemovingElements.Add(parser.XmlContext.Element);
                            }
                        }
                        return value ?? string.Empty;
                    },
                },
                //descriptors
                new TemplatorKeyword(KeywordComments)
                {
                    Description = "Put a comment of the TextHolder in the parsed result holder list",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The comment string","" )
                    },
                    Parse = ((parser, s) => parser.Context.Holder[KeywordComments] = s)
                },
                new TemplatorKeyword(KeywordDisplayName)
                {
                    Description = "A display name for the TextHolder",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The display name string","" )
                    },
                },
                //Keyword Expand
                new TemplatorKeyword(KeywordTruncate)
                {
                    Description = "If a value string's length is longer than the max value of keyword '{0}' and this keyword exists, the string will be truncated to that length without producing an error".FormatInvariantCulture(KeywordLength),
                },
                new TemplatorKeyword(KeywordFill)
                {
                    Description = "Specifies a character used to append to the output if keyword '{0}' is specified and the length is less than the fixed-length.".FormatInvariantCulture(KeywordFixedLength),
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The char used to fill","" )
                    },
                },
                new TemplatorKeyword(KeywordPrefill)
                {
                    Description = "Specifies a character used to fill the output if keyword '{0}' is specified and the length is less than the fixed-length, this will pre-fill the string instead of append".FormatInvariantCulture(KeywordFixedLength),
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The char used to fill","" )
                    },
                },
                new TemplatorKeyword(KeywordAppend)
                {
                    
                },
                //Align minCount
                new TemplatorKeyword(KeywordAlignCount)
                {
                    Description = "If multiple arrays/repeat/collections are provided in the input and this option exists, the output will make sure every array/repeat/collection's length is the same as the max length of the input, the empty part will be filled with null values without producing errors",
                    Params = new List<Pair<string, string>>
                    {
                        new Pair<string, string>("The holder name of the parent array/repeat/collection","" )
                    },
                    Parse = ((parser, s) =>
                    {
                        if (parser.NoInput)
                        {
                            return;
                        }
                        if (s.IsNullOrWhiteSpace())
                        {
                            parser.Context.State.Error = true;
                            if (parser.Config.IgnoreUnknownParam || parser.Config.ContinueOnError)
                            {
                                return;
                            }
                            throw new TemplatorParamsException();
                        }
                        parser.Context.Holder[KeywordAlignCount] = s;
                        var childInputs = TemplatorUtil.GetChildCollection(parser.ParentContext.Input, s, parser.Config);
                        if (!childInputs.IsNullOrEmpty())
                        {
                            TemplatorUtil.SetInputCount(parser, parser.Context.Holder, childInputs.Max(c =>
                            {
                                var child = TemplatorUtil.GetChildCollection(c, parser.Context.Holder.Name, parser.Config);
                                return child?.Length ?? 0;
                            }));
                        }
                    })
                },
            }).Where(k => !k.Name.IsNullOrEmpty()).ToDictionary(k => k.Name);
            foreach (var c in _customKeywords.EmptyIfNull().Where(k => !k.Name.IsNullOrEmpty()))
            {
                Keywords.AddOrOverwrite(c.Name, c);
            }
            foreach (var custom in CustomKeywordNames.EmptyIfNull())
            {
                Keywords.AddOrSkip(custom, new TemplatorKeyword(custom));
            }
            var index = 1;
            foreach (var key in Keywords.Values)
            {
                key.Priority = key.Priority > 0 ? key.Priority : index += KeywordPriorityIncreamental;
            }
            EscapePrefix = EscapePrefix == String.Empty ? null : EscapePrefix;
        }
    }
}
