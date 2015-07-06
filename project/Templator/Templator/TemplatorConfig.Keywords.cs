﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using DotNetUtils;

namespace Templator
{
    public partial class TemplatorConfig
    {
        public void PrepareKeywords()
        {
            KeyWords = (new List<TemplatorKeyWord>()
            {
                //Structure keywords
                new TemplatorKeyWord(KeyWordRepeat){
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) => value == null ? null : parser.InXmlManipulation() ? value : String.Empty, 
                    PostParse = (parser, parsedHolder) =>
                    {
                        KeyWords[KeyWordRepeatBegin].PostParse(parser, parsedHolder);
                        if (parser.InXmlManipulation())
                        {
                            KeyWords[KeyWordRepeatEnd].PostParse(parser, parsedHolder);
                        }
                        return false;
                    } 
                },
                new TemplatorKeyWord(KeyWordRepeatBegin){
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) => value == null ? null : parser.InXmlManipulation() ? value : String.Empty, 
                    PostParse = (parser, parsedHolder) =>
                    {
                        var childInputs = parser.Context.Input.GetChildCollection(parsedHolder.Name, parser.Config);
                        IDictionary<string, object> input = null;
                        var l = parser.StackLevel + parsedHolder.Name;
                        var inputIndex = (int?)parser.Context[l + "InputIndex"] ?? 0;
                        var inputCount = 0;
                        if (childInputs.IsNullOrEmpty())
                        {
                            parser.Context[l + "InputCount"] = 0;
                            parser.Context[l + "InputIndex"] = inputIndex;
                            input = parser.Context.Input == null ? null : new Dictionary<string, object>() { { ReservedKeyWordParent, parser.Context.Input } };
                        }
                        else
                        {
                            input = childInputs[inputIndex++];
                            parser.Context[l + "InputIndex"] = inputIndex;
                            parser.Context[l + "InputCount"] = inputCount = childInputs.Length;
                        }
                        parser.Context.Holders.AddOrSkip(parsedHolder.Name, parsedHolder);
                        if (parser.InXmlManipulation())
                        {
                            parser.ParentXmlContext.OnAfterParsingElement = null;
                            if (inputIndex < inputCount )
                            {
                                parser.ParentXmlContext[l + "XmlElementIndex"] = parser.ParentXmlContext.ElementIndex;

                                parser.ParentXmlContext.OnBeforeParsingElement = p =>
                                {
                                    var ll = p.StackLevel -1  + parsedHolder.Name;
                                    if ((int)p.ParentContext[ll + "InputCount"] > (int)p.ParentContext[ll + "InputIndex"])
                                    {
                                        var startIndex = (int)p.XmlContext[ll + "XmlElementIndex"];
                                        startIndex += p.XmlContext.ElementIndex - startIndex;
                                        var element = new XElement(p.XmlContext.ElementList[startIndex]);
                                        p.XmlContext.ElementList[startIndex] = element;
                                        p.XmlContext.Element.Add(element);
                                    }
                                };

                                var newElement = new XElement(parser.ParentXmlContext.ElementList[parser.ParentXmlContext.ElementIndex]);
                                parser.ParentXmlContext.ElementList[parser.ParentXmlContext.ElementIndex] = newElement;
                                parser.ParentXmlContext.Element.Add(newElement);
                            }
                        }
                        
                        parser.PushContext(input, parsedHolder, parsedHolder.IsOptional());
                        return false;
                    } 
                },
                new TemplatorKeyWord(KeyWordRepeatEnd){
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) => String.Empty,
                    PostParse = (parser, parsedHolder) =>
                    {
                        if (parser.InXmlManipulation())
                        {
                            parser.ParentXmlContext.OnAfterParsingElement = p =>
                            {
                                p.PopContext();
                                var ll = p.StackLevel + parsedHolder.Name;
                                if ((int)p.Context[ll + "InputCount"] > (int)p.Context[ll + "InputIndex"])
                                {
                                    p.XmlContext.ElementIndex = (int)p.XmlContext[ll + "XmlElementIndex"] - 1;
                                }
                                else
                                {
                                    p.Context[ll + "InputIndex"] = null;
                                    p.XmlContext.OnBeforeParsingElement = null;
                                }
                                p.XmlContext.OnAfterParsingElement = null;
                            };
                            parser.ParentXmlContext.OnBeforeParsingElement = null;
                        }
                        else
                        {
                            var position = parser.Context.ParentHolder.Position;
                            parser.PopContext();
                            var l = parser.StackLevel + parsedHolder.Name;
                            if ((int)parser.Context[l + "InputCount"] > (int)parser.Context[l + "InputIndex"])
                            {
                                parser.Context.Text.Position = position;
                            }
                            else
                            {
                                parser.Context[l + "InputIndex"] = null;
                            }
                        }
                        return false;
                    }
                },
                //Lookup Keywords
                new TemplatorKeyWord(KeyWordRefer)
                {
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        var refer = (string)holder[KeyWordRefer];
                        return parser.GetValue(refer, parser.Context.Input);
                    },
                    Parse = (parser, str) =>
                    {
                        if (str.IsNullOrWhiteSpace())
                        {
                            throw new TemplatorParamsException();
                        }
                        parser.ParsingHolder[KeyWordRefer] = str;
                    }
                },
                new TemplatorKeyWord(KeyWordSeekup)
                {
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (value == null)
                        {
                            var level = holder[KeyWordSeekup].ParseDecimalNullable() ?? 0;
                            var input = parser.Context.Input;
                            while (level-- > 0 && input != null)
                            {
                                input = (IDictionary<string, object>) input[ReservedKeyWordParent];
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
                        parser.ParsingHolder[KeyWordSeekup] = str.ParseIntParam(1);
                    })
                },
                new TemplatorKeyWord(KeyWordJs)
                {
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyWord(KeyWordSum)
                {
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string)holder[KeyWordSum];
                        return parser.Aggregate(holder, aggregateField, parser.Context.Input, (agg, num) => (num.ParseDecimalNullable() ?? 0) + (agg.ParseDecimalNullable(0) ?? 0)).DecimalToString();
                    }
                },
                new TemplatorKeyWord(KeyWordCount)
                {
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string) holder[KeyWordCount];
                        return parser.Aggregate(holder, aggregateField, parser.Context.Input, (agg, array) => (agg.ParseDecimalNullable(0) ?? 0) + (array is object[] ? ((object[])array).Length : (array.ParseDecimalNullable(0) ?? 0))).DecimalToString();
                    }
                },
                new TemplatorKeyWord(KeyWordMulti)
                {
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        var aggregateField = (string) holder[KeyWordCount];
                        return parser.Aggregate(holder, aggregateField, parser.Context.Input, (agg, num) => (num.ParseDecimalNullable() ?? 1) * (agg.ParseDecimalNullable() ?? 1)).DecimalToString();
                    }
                },
                new TemplatorKeyWord(KeyWordOptional)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    OnGetValue = (holder, parser, value) => value ?? holder[KeyWordOptional],
                },
                //Validation Keywords
                new TemplatorKeyWord(KeyWordRegex)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var regStr = (string) holder[KeyWordRegex];
                        if (!regStr.IsNullOrWhiteSpace())
                        {
                            Regex reg = null;
                            var d = Regexes.ContainsKey(regStr);
                            reg = d ? Regexes[regStr] : new Regex(regStr, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                            if (!reg.IsMatch(Convert.ToString(value)))
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
                    }
                },
                new TemplatorKeyWord(KeyWordLength)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        string str = null;
                        str = holder.ContainsKey(KeyWordNumber) ? Convert.ToString(value.DecimalToString() ?? value) : Convert.ToString(value);   
                        var length = str.Length;
                        int? maxLength = null;
                        var customLength = (string)holder[KeyWordLength];
                        if (!customLength.IsNullOrWhiteSpace())
                        {
                            if (customLength.Contains("-"))
                            {
                                var arr = customLength.Split('-');
                                if (arr.Length != 2)
                                {
                                    throw new TemplatorParamsException("Invalid length defined");
                                }
                                int min, max;
                                if (int.TryParse(arr[0], out min) && int.TryParse(arr[1], out max))
                                {
                                    if (max >= length && min <= length)
                                    {
                                        return str;
                                    }
                                    if (max < length)
                                    {
                                        maxLength = max;
                                    }
                                    goto invalidLength;
                                }
                            }
                            else if (customLength.Contains(";"))
                            {
                                foreach (var f in customLength.Split(';'))
                                {
                                    var l = 0;
                                    if (int.TryParse(f, out l))
                                    {
                                        if (l == length)
                                        {
                                            return str;
                                        }
                                    }
                                    else
                                    {
                                        throw new TemplatorParamsException("Invalid length defined");
                                    }
                                }
                                goto invalidLength;
                            }
                            else
                            {
                                var l = 0;
                                if (int.TryParse(customLength, out l))
                                {
                                    if (l == length)
                                    {
                                        return str;
                                    }
                                }
                                else
                                {
                                    throw new TemplatorParamsException("Invalid length defined");
                                }
                                goto invalidLength;
                            }
                            throw new TemplatorParamsException("Invalid length defined");
                        }
                        invalidLength:
                        if (maxLength.HasValue && holder.ContainsKey(KeyWordTruncate))
                        {
                            str = str.Substring(0, maxLength.Value);
                            return str;
                        }
                        parser.LogError("Invalid Field length: '{0}', value: {1}, valid length: {2}.", "HolderName", value, customLength);
                        return null;
                    }
                },
                //new TemplatorKeyWord(KeyWordSelect){},
                //new TemplatorKeyWord(KeyWordExpression){},
                new TemplatorKeyWord(KeyWordMin)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var min = (decimal)holder[KeyWordMin];
                        if (holder.ContainsKey(KeyWordNumber))
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
                        parser.ParsingHolder[KeyWordMin] = str.ParseNumberParam();
                    })
                },
                new TemplatorKeyWord(KeyWordMax)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var max = (decimal) holder[KeyWordMax];
                        if (holder.ContainsKey(KeyWordNumber))
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
                        parser.ParsingHolder[KeyWordMax] = str.ParseNumberParam();
                    })
                },
                //DataType/format Keywords
                new TemplatorKeyWord(KeyWordBit){OnGetValue = (holder, parser, value) => value == null ? null : String.Empty},
                new TemplatorKeyWord(KeyWordEnum){
                    OnGetValue = (holder, parser, value) =>
                    {
                        var typeName = (string)holder[KeyWordEnum];
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
                            parser.ParsingHolder[KeyWordEnum] = str;
                        }
                        else
                        {
                            throw new TemplatorParamsException();
                        }
                    })
                },
                new TemplatorKeyWord(KeyWordNumber)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDecimalNullable();
                        if (d == null)
                        {
                            parser.LogError("'{0}' is not a number in '{1}'", value, holder.Name);
                            return null;
                        }
                        var format = (string)holder[KeyWordNumber];
                        return d.Value.ToString(format.IsNullOrWhiteSpace() ? "G29" : format);
                    }
                },
                new TemplatorKeyWord(KeyWordDateTime)
                {

                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDateTimeNullable();
                        if (d == null)
                        {
                            parser.LogError("'{0}' is not a valid DateTime in '{1}'", value, holder.Name);
                            return null;
                        }
                        var format = (string)holder[KeyWordDateTime];
                        if (format.IsNullOrWhiteSpace())
                        {
                            return d;
                        }
                        return d.Value.ToString(format);
                    }
                },
                //Format Keywords
                new TemplatorKeyWord(KeyWordEven)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDecimalNullable();
                        return d.HasValue ? (object) decimal.Round(d.Value, MidpointRounding.ToEven) : null;
                    }
                },
                new TemplatorKeyWord(KeyWordAwayFromZero)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var d = value.ParseDecimalNullable();
                        return d.HasValue ? (object) decimal.Round(d.Value, (int?)holder[KeyWordAwayFromZero].ParseDecimalNullable() ?? 2, MidpointRounding.AwayFromZero) : null;
                    }
                },
                new TemplatorKeyWord(KeyWordFormat)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (null == value)
                        {
                            return null;
                        }
                        var pattern = (string)holder[KeyWordFormat];
                        return pattern.Format(value);
                    }
                },
                new TemplatorKeyWord(KeyWordMap)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var dict = (IDictionary<string, string>)holder[KeyWordMap];
                        var str = Convert.ToString(value);
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
                        parser.ParsingHolder[KeyWordMap] = ret;
                    }
                },
                new TemplatorKeyWord(KeyWordReplace)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    },
                    Parse = (parser, str) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyWord(KeyWordTransform)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var str = Convert.ToString(value);
                        switch ((string)holder[KeyWordTransform])
                        {
                            case "Lower":
                                return str.ToLower();
                            case "Upper":
                                return str.ToUpper();
                        }
                        return str;
                    }
                },
                new TemplatorKeyWord(KeyWordUpper)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        value = Convert.ToString(value).ToUpper();
                        return value;
                    }
                },
                new TemplatorKeyWord(KeyWordTrim)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        var str = Convert.ToString(value);
                        var trim = (string)holder[KeyWordTrim];
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
                new TemplatorKeyWord(KeyWordHolder)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        if (parser.XmlContext != null && parser.XmlContext.Element != null)
                        {
                            parser.RemovingElements.Add(parser.XmlContext.Element);
                        }
                        return value == null ? null : String.Empty;
                    }
                },
                new TemplatorKeyWord(KeyWordRemoveChar)
                {
                    OnGetValue = (holder, parser, value) => Convert.ToString(value).RemoveCharacter((char[]) holder[KeyWordRemoveChar]),
                    Parse = ((parser, str) =>
                    {
                        if (str.Length != 1)
                        {
                            throw new TemplatorParamsException();
                        }
                        parser.ParsingHolder[KeyWordRemoveChar] = str.ToCharArray();
                    })
                },
                new TemplatorKeyWord(KeyWordFixedLength)
                {
                    HandleNullOrEmpty = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        var length = (int) holder[KeyWordFixedLength];
                        return Convert.ToString(value ?? String.Empty)
                            .ToFixLength(length, (((string)holder[KeyWordFill] ?? (string)holder[KeyWordFill] ?? (string)holder[KeyWordPrefill]).NullIfEmpty() ?? " ").First(),
                                !holder.ContainsKey(KeyWordPrefill));
                    },
                    Parse = ((parser, str) =>
                    {
                        parser.ParsingHolder[KeyWordFixedLength] = str.ParseIntParam();
                    })
                },
                //document manupulation keywords
                //new TemplatorKeyWord(KeyWordArrayEnd){},
                //new TemplatorKeyWord(KeyWordArray){},
                //new TemplatorKeyWord(KeyWordDefault){},
                new TemplatorKeyWord(KeyWordIfnot)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        var ifQuery = (string)holder[KeyWordIfnot];
                        if (value.IsNullOrEmpty())
                        {
                            parser.RemovingElements.Add(ifQuery.IsNullOrWhiteSpace()
                            ? parser.XmlContext.Element
                            : parser.XmlContext.Element.XPathSelectElement(ifQuery));
                        }
                        return value ?? String.Empty;
                    }
                },
                new TemplatorKeyWord(KeyWordEnumElement)
                {
                    Parse = (parser, str) =>
                    {
                        parser.ParsingHolder.KeyWords.Add(KeyWords[KeyWordEnum].Create());
                        parser.ParsingHolder[KeyWordEnum] = str;
                        parser.ParsingHolder.KeyWords.Add(KeyWords[KeyWordElementName].Create());
                    }
                },
                new TemplatorKeyWord(KeyWordElementName)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        parser.XmlContext.Element.Name = parser.XmlContext.Element.Name.Namespace + Convert.ToString(value);
                        return String.Empty;
                    }
                },
                new TemplatorKeyWord(KeyWordAttributeThen)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyWord(KeyWordAttributeIfnot)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyWord(KeyWordAttributeIf)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyWord(KeyWordAttributeName)
                {
                    OnGetValue = (holder, parser, value) =>
                    {
                        throw new NotImplementedException();
                    }
                },
                new TemplatorKeyWord(KeyWordThen)
                {
                    HandleNullOrEmpty = true,
                },
                new TemplatorKeyWord(KeyWordIf)
                {
                    HandleNullOrEmpty = true,
                    IndicatesOptional = true,
                    OnGetValue = (holder, parser, value) =>
                    {
                        var eav = value;
                        var name = (string) holder[KeyWordIf];
                        if (!name.IsNullOrEmpty())
                        {
                            eav = parser.GetValue(name, parser.Context.Input, (int?) holder[KeyWordSeekup] ?? 0);
                        }
                        if (eav.IsNullOrEmpty())
                        {
                            parser.RemovingElements.Add(parser.XmlContext.Element);
                        }
                        return value ?? string.Empty;
                    },
                },
                //discriptors
                new TemplatorKeyWord(KeyWordComments)
                {
                    Parse = ((parser, s) => parser.ParsingHolder[KeyWordComments] = s)
                },
                new TemplatorKeyWord(KeyWordDisplayName)
                {
                },
                new TemplatorKeyWord(KeyWordTruncate){},
                new TemplatorKeyWord(KeyWordFill){},
                new TemplatorKeyWord(KeyWordPrefill){},
                new TemplatorKeyWord(KeyWordAppend){},
            }).ToDictionary(k => k.Name);
            var index = 1;
            foreach (var key in KeyWords.Values)
            {
                key.Preority = key.Preority > 0 ? key.Preority : index++;
            }
        }
    }
}
