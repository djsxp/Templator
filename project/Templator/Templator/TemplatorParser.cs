﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.Schema;
using DotNetUtils;
using Irony.Parsing;

namespace Templator
{
    public class TemplatorParser
    {
        public bool Csv;
        public TemplatorConfig Config;
        public TemplatorKeyword ParsingKeyword;
        public TextHolder ParsingHolder;
        public HolderParseState State;
        public IList<XElement> RemovingElements = new List<XElement>();

        public IDictionary<string, TextHolder> Holders = new Dictionary<string, TextHolder>();

        public TemplatorXmlParsingContext XmlContext;
        public TemplatorParsingContext Context;
        public Stack<TemplatorParsingContext> Stack = new Stack<TemplatorParsingContext>();
        public Stack<TemplatorXmlParsingContext> XmlStack = new Stack<TemplatorXmlParsingContext>();

        public TemplatorXmlParsingContext ParentXmlContext
        {
            get { return XmlStack.Peek(); }
        }
        public TemplatorParsingContext ParentContext
        {
            get { return Stack.Peek(); }
        }

        public int StackLevel
        {
            get { return Stack.Count; }
        }

        public TemplatorParser(TemplatorConfig config)
        {
            Config = config;
            var grammar = new TemplatorGrammar(Config);
            GrammarParser = new Parser(grammar);
            if (!config.SyntaxCheckOnly)
            {
                GrammarParser.Context.TokenCreated += OnGrammerTokenCreated;
            }
        }

#region Grammar

        private int _parsingStart = 0;
        public readonly Parser GrammarParser;
        public ParseTree GrammarParseTree;
        public string TemplateString;

        public virtual void OnHolderCreated(string text, TextHolder holder, ParsingContext grammarContext)
        {
            if (holder != null)
            {
                if (ParsingHolder.Keywords.Any(k => k.Name == Config.KeywordRepeatEnd))
                {
                    PopContext();
                }
                else
                {
                    Context.Holders[ParsingHolder.Name] = ParsingHolder;
                    if (ParsingHolder.Keywords.Any(k => k.Name == Config.KeywordRepeat || k.Name == Config.KeywordRepeatBegin))
                    {
                        PushContext(null, holder);
                    }
                }
            }
            AppendResult(text);
        }

        public virtual void OnGrammerTokenCreated(object sender, ParsingEventArgs e)
        {
            var context = (ParsingContext)sender;
            if (context.HasErrors)
            {
                return;
            }
            var v = context.CurrentToken.ValueString;
            if (context.CurrentToken.Terminal != null)
            {
                var t = context.CurrentToken.Terminal.Name;
                if (context.OpenBraces.Count == 0)
                {
                    if (ParsingHolder != null)
                    {
                        ParsingHolder.Position = _parsingStart;
                        var end = context.PreviousToken.Location.Position + context.PreviousToken.Length;
#if DEBUG
                        ParsingHolder.SourceText = TemplateString.Substring(_parsingStart, end - _parsingStart);
#endif
                        OnHolderCreated(ParsingHolder.SourceText, ParsingHolder, context);
                        ParsingHolder = null;
                    }
                    if (t == Config.TermText)
                    {
                        OnHolderCreated(v, null, context);
                    }
                    _parsingStart = context.CurrentToken.Location.Position;
                    return;
                }
                if (ParsingHolder == null)
                {
                    ParsingHolder = new TextHolder();
                }

                if (t == Config.TermCategory)
                {
                    ParsingHolder.Category = v;
                }
                else if (t == Config.TermName)
                {
                    ParsingHolder.Name = v;
                }
                else if (t == Config.TermValue)
                {
                    if (ParsingKeyword == null)
                    {
                        //throw new TemplatorSyntaxException();
                    }
                    else
                    {
                        ParsingKeyword.Parse(this, v);
                        ParsingHolder.Keywords.Add(ParsingKeyword);
                        ParsingKeyword = null;
                    }
                }
                else if (Config.Keywords.ContainsKey(t))
                {
                    if (ParsingKeyword != null)
                    {
                        throw new TemplatorSyntaxException();
                    }
                    ParsingKeyword = Config.Keywords[v];
                    ParsingHolder.Keywords.Add(ParsingKeyword);
                    ParsingKeyword = null;
                }
            }
        }

        public virtual IDictionary<string, TextHolder> GrammarCheck(string template, string fileName)
        {
#if DEBUG
            TemplateString = template;
#endif
            ParsingHolder = null;
            ParsingKeyword = null;
            PushContext(null, null);
            GrammarParseTree = GrammarParser.Parse(template, fileName);
            return Context.Holders;
        }

        #endregion Grammer
        
        public void StartOver(bool clearHolders = true)
        {
            RemovingElements.Clear();
            Stack.Clear();
            XmlStack.Clear();
            XmlContext = null;
            Context = null;
            Csv = false;
            if (clearHolders)
            {
                Holders.Clear();
            }
        }
        public void RequireInput(object sender, TemplateEventArgs args)
        {
            var recursiveCheckKey = args.Holder.Name + "$Processing";
            if ((bool?)Context[recursiveCheckKey] == true)
            {
                Context[recursiveCheckKey] = null;
                return;
            }
            Context[recursiveCheckKey] = true;
            if (Config.RequireInput != null)
            {
                Config.RequireInput(this, args);
            }
            Context[recursiveCheckKey] = null;
        }


#region parsing
        public virtual string ParseCsv(string src, IDictionary<string, object> input, IDictionary<string, TextHolder> preparsedHolders = null)
        {
            Csv = true;
            return ParseText(src, input, preparsedHolders);
        }

        public virtual string ParseText(string src, IDictionary<string, object> input, IDictionary<string, TextHolder> preparsedHolders = null)
        {
            PushContext(input, null);
            Context.Text = new SeekableString(src, Config.LineBreakOption);
            Context.PreparsedHolders= preparsedHolders;
            Context.Result.Clear();
            ParseTextInternal(input);
            CollectHolderResults();
            return Context.Result.ToString();
        }

        public virtual XElement ParseXml(XDocument doc, IDictionary<string, object> input, IDictionary<string, TextHolder> preparsedHolders = null, XmlSchemaSet schemaSet = null)
        {
            if (schemaSet != null)
            {
                doc.Validate(schemaSet, (o, e) => { }, true);
            }
            return ParseXml(doc.Root, input, preparsedHolders);
        }

        public virtual XElement ParseXml(XElement rootElement, IDictionary<string, object> input, IDictionary<string, TextHolder> preparsedHolders = null)
        {
            XmlContext = new TemplatorXmlParsingContext(){Element = rootElement};
            PushContext(input, null);
            Context.PreparsedHolders = preparsedHolders;
            ParseXmlInternal(rootElement);
            foreach (var removingElement in RemovingElements)
            {
                if (removingElement != null && removingElement.Parent != null)
                {
                    removingElement.Remove();
                }
            }
            CollectHolderResults();
            return XmlContext.Element;
        }
        public void ParseXmlInternal(XElement element)
        {
            if (XmlContext.OnBeforeParsingElement != null)
            {
                XmlContext.OnBeforeParsingElement(this);
            }
            PushXmlContext(element);
            foreach (var a in element.Attributes().OrderBy(a => a.Name != Config.XmlReservedAttributeName))
            {
                XmlContext.Attribute = a;
                Context.Result.Clear();
                Context.Text = new SeekableString(a.Value);
                var hasHolder = ParseTextInternal(Context.Input);
                if (hasHolder)
                {
                    a.Value = Context.Result.ToString();
                }
                if (a.Name == Config.XmlReservedAttributeName)
                {
                    a.Remove();
                }
                XmlContext.Attribute = null;
            }
            if (element.HasElements)
            {
                for (XmlContext.ElementIndex = 0; XmlContext.ElementIndex < XmlContext.ElementList.Count; XmlContext.ElementIndex++)
                {
                    ParseXmlInternal(XmlContext.ElementList[XmlContext.ElementIndex]);
                    if (XmlContext.OnAfterParsingElement != null)
                    {
                        XmlContext.OnAfterParsingElement(this);
                    }
                }
            }
            else
            {
                Context.Text = new SeekableString(element.Value, Config.LineBreakOption);
                Context.Result.Clear();
                var hasHolder = ParseTextInternal(Context.Input);
                if (hasHolder)
                {
                    element.Value = Context.Result.ToString();
                }
            }
            PopXmlContext();
        }

        public virtual bool ParseTextInternal(IDictionary<string, object> input)
        {
            var hasHolder = false;
            Context.Input = input;
            State = new HolderParseState();
            while (!Context.Text.Eof || State.End) 
            {
                if (HolderParsingStates.States.ContainsKey(State))
                {
                    var fun = HolderParsingStates.States[State];
                    var holder = fun(this);
                    if (holder != null)
                    {
                        hasHolder = true;
                        Context.Holders.AddOrSkip(holder.Name, holder);
                    }
                }
                else
                {
                    throw new TemplatorUnexpecetedStateException();
                }
            }
            return hasHolder;
        }
#endregion parsing

#region utils
        public IDictionary<string, object>[] GetChildInputs(string key)
        {
            return TemplatorUtil.GetChildCollection(Context.Input, key, Config);
        }

        public bool HasInput(string key)
        {
            return Context.Input.ContainsKey(key);
        }

        public object GetInputValue(string key, object defaultRet = null)
        {
            return Context.Input.GetOrDefault(key, defaultRet);
        }

        public T GetInputValue<T>(string key, T defaultRet = default(T))
        {
            return Context.Input.GetOrDefault(key, defaultRet).SafeConvert(defaultRet, Config.DateFormat);
        }

        public T GetValue<T>(string key, T defaultRet = default(T), int seekUp = 0)
        {
            return TemplatorUtil.GetValue(this, key, Context.Input, seekUp).SafeConvert<T>(default(T), Config.DateFormat);
        }

        public T GetValue<T>(TextHolder key, T defaultRet = default(T), int seekUp = 0)
        {
            return TemplatorUtil.GetValue(this, key, Context.Input).SafeConvert<T>(default(T), Config.DateFormat);
        }
        
        public void CacheValue(string key, object value, bool overWirteIfExists = false)
        {
            if (overWirteIfExists)
            {
                Context.Input.AddOrOverwrite(key, value);
            }
            else
            {
                Context.Input.AddOrSkip(key, value);
            }
        }

        public TextHolder GetHolder(string name)
        {
            if (Context.PreparsedHolders != null)
            {
                return Context.PreparsedHolders.GetOrDefault(name) ?? new TextHolder(name);
            }
            return new TextHolder(name);
        }
#endregion utils

#region Contexts
        public void PushContext(IDictionary<string, object> input, TextHolder parentHolder, bool skipOutput = false, bool disableLogging = false)
        {
            var holderDefinitions = parentHolder == null ? null : Context.PreparsedHolders.GetOrDefault(parentHolder.Name);
            var newC = new TemplatorParsingContext(skipOutput)
            {
                Input = input,
                ParentHolder = parentHolder,
                Logger = disableLogging ? null : Config.Logger,
                Text = Context == null ? null : Context.Text,
                PreparsedHolders = holderDefinitions == null ? null : holderDefinitions.Children
                
            };

            if (Context == null)
            {
                Context = newC;
                return;
            }
            Stack.Push(Context);
            Context = newC;
        }

        public void PushXmlContext(XElement element)
        {
            var newC = new TemplatorXmlParsingContext
            {
                ElementList = element.Elements().ToArray(),
                Element = element,
                ElementIndex = 0
            };
            XmlStack.Push(XmlContext);
            XmlContext = newC;
        }

        public void PopContext()
        {
            var c = Context;
            Context = Stack.Pop();
            if (c.ParentHolder != null)
            {
                c.ParentHolder.Children = c.Holders;
            }
            AppendResult(c.Result);
        }

        public void PopXmlContext()
        {
            XmlContext = XmlStack.Pop();
        }
        #endregion Contexts
        public void LogError(string pattern, params object[] args)
        {
            if (Context.Logger != null )
            {
                Context.Logger.LogError(pattern, args);
            }
        }
#region results
        public void AppendResult(object value)
        {
            if (Context.Result != null)
            {
                Context.Result.Append(value);
            }
        }

        private void CollectHolderResults(string mergeInto = null)
        {
            TemplatorUtil.MergeHolders(Holders, Context.Holders.Values, mergeInto);
        }
#endregion results
    }
}
