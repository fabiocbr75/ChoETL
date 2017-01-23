﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ChoETL
{
    public class ChoFixedLengthRecordConfiguration : ChoFileRecordConfiguration
    {
        public ChoFixedLengthFileHeaderConfiguration FileHeaderConfiguration
        {
            get;
            set;
        }

        public List<ChoFixedLengthRecordFieldConfiguration> RecordFieldConfigurations
        {
            get;
            private set;
        }
        public int RecordLength
        {
            get;
            set;
        }
        public ChoFixedLengthFieldDefaultSizeConfiguation FixedLengthFieldDefaultSizeConfiguation
        {
            get;
            set;
        }

        internal Dictionary<string, ChoFixedLengthRecordFieldConfiguration> RecordFieldConfigurationsDict
        {
            get;
            private set;
        }

        public ChoFixedLengthRecordConfiguration(Type recordType = null) : base(recordType)
        {
            RecordFieldConfigurations = new List<ChoFixedLengthRecordFieldConfiguration>();

            if (recordType != null)
            {
                Init(recordType);
            }

            FileHeaderConfiguration = new ChoFixedLengthFileHeaderConfiguration(recordType, Culture);
        }

        protected override void Init(Type recordType)
        {
            base.Init(recordType);

            ChoFixedLengthRecordObjectAttribute recObjAttr = ChoType.GetAttribute<ChoFixedLengthRecordObjectAttribute>(recordType);
            if (recObjAttr != null)
            {
                RecordLength = recObjAttr.RecordLength;
            }

            DiscoverRecordFields(recordType);
        }

        public override void MapRecordFields<T>()
        {
            DiscoverRecordFields(typeof(T));
        }

        public override void MapRecordFields(Type recordType)
        {
            DiscoverRecordFields(recordType);
        }

        private void DiscoverRecordFields(Type recordType)
        {
            if (recordType != typeof(ExpandoObject))
            {
                RecordFieldConfigurations.Clear();

                foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(recordType).AsTypedEnumerable<PropertyDescriptor>().Where(pd => pd.Attributes.OfType<ChoFixedLengthRecordFieldAttribute>().Any()))
                {
                    //if (!pd.PropertyType.IsSimple())
                    //    throw new ChoRecordConfigurationException("Property '{0}' is not a simple type.".FormatString(pd.Name));
                    var obj = new ChoFixedLengthRecordFieldConfiguration(pd.Name, pd.Attributes.OfType<ChoFixedLengthRecordFieldAttribute>().First());
                    obj.FieldType = pd.PropertyType;
                    RecordFieldConfigurations.Add(obj);
                }
            }
        }

        public override void Validate(object state)
        {
            base.Validate(state);

            string line = ((Tuple<int, string>)state).Item2;

            //Validate Header
            if (FileHeaderConfiguration != null)
            {
                if (FileHeaderConfiguration.FillChar != null)
                {
                    if (FileHeaderConfiguration.FillChar.Value == ChoCharEx.NUL)
                        throw new ChoRecordConfigurationException("Invalid '{0}' FillChar specified.".FormatString(FileHeaderConfiguration.FillChar));
                    if (EOLDelimiter.Contains(FileHeaderConfiguration.FillChar.Value))
                        throw new ChoRecordConfigurationException("FillChar [{0}] can't be one of EOLDelimiter characters [{1}]".FormatString(FileHeaderConfiguration.FillChar.Value, EOLDelimiter));
                    if ((from comm in Comments
                         where comm.Contains(FileHeaderConfiguration.FillChar.Value.ToString())
                         select comm).Any())
                        throw new ChoRecordConfigurationException("One of the Comments contains FillChar. Not allowed.");
                }
            }

            //string[] headers = state as string[];
            if (AutoDiscoverColumns
                && RecordFieldConfigurations.Count == 0 /*&& headers != null*/)
            {
                if (RecordType != null && RecordType != typeof(ExpandoObject)
                    && TypeDescriptor.GetProperties(RecordType).AsTypedEnumerable<PropertyDescriptor>().Where(pd => pd.Attributes.OfType<ChoFixedLengthRecordFieldAttribute>().Any()).Any())
                {
                    int startIndex = 0;
                    int size = 0;
                    foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(RecordType).AsTypedEnumerable<PropertyDescriptor>().Where(pd => pd.Attributes.OfType<ChoFixedLengthRecordFieldAttribute>().Any()))
                    {
                        //if (!pd.PropertyType.IsSimple())
                        //    throw new ChoRecordConfigurationException("Property '{0}' is not a simple type.".FormatString(pd.Name));

                        if (FixedLengthFieldDefaultSizeConfiguation == null)
                            size = ChoFixedLengthFieldDefaultSizeConfiguation.Instance.GetSize(pd.PropertyType);
                        else
                            size = FixedLengthFieldDefaultSizeConfiguation.GetSize(pd.PropertyType);

                        var obj = new ChoFixedLengthRecordFieldConfiguration(pd.Name, startIndex, size);
                        obj.FieldType = pd.PropertyType;
                        RecordFieldConfigurations.Add(obj);

                        startIndex += size;
                    }

                    RecordLength = startIndex;
                }
                else if (!line.IsNullOrEmpty())
                {
                    if (RecordType == typeof(ExpandoObject))
                    {
                        foreach (var item in DiscoverColumns(line))
                        {
                            var obj = new ChoFixedLengthRecordFieldConfiguration(item.Item1.NTrim(), item.Item2, item.Item3);
                            RecordFieldConfigurations.Add(obj);
                        }
                    }
                    else
                    {
                        Tuple<string, int, int>[] tuples = DiscoverColumns(line);
                        int index = 0;
                        foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(RecordType).AsTypedEnumerable<PropertyDescriptor>())
                        {
                            if (index < tuples.Length)
                            {
                                var obj = new ChoFixedLengthRecordFieldConfiguration(FileHeaderConfiguration.HasHeaderRecord ? tuples[index].Item1.NTrim() : pd.Name, tuples[index].Item2, tuples[index].Item3);
                                RecordFieldConfigurations.Add(obj);
                                index++;
                            }
                            else
                                break;
                        }
                    }
                }
            }

            if (RecordFieldConfigurations.Count == 0)
                throw new ChoRecordConfigurationException("No record fields specified.");

            //Derive record length from fields
            if (RecordLength <= 0)
            {
                int maxStartIndex = RecordFieldConfigurations.Max(f => f.StartIndex);
                int maxSize = RecordFieldConfigurations.Where(f => f.StartIndex == maxStartIndex).Max(f1 => f1.Size.Value);
                var fc = RecordFieldConfigurations.Where(f => f.StartIndex == maxStartIndex && f.Size.Value == maxSize).FirstOrDefault();
                if (fc != null)
                {
                    RecordLength = fc.StartIndex + fc.Size.Value;
                }
            }

            if (RecordLength <= 0)
                throw new ChoRecordConfigurationException("RecordLength must be > 0");

            //Check if any field has empty names
            if (RecordFieldConfigurations.Where(i => i.FieldName.IsNullOrWhiteSpace()).Count() > 0)
                throw new ChoRecordConfigurationException("Some fields has empty field name specified.");

            //Check field names for duplicate
            string[] dupFields = RecordFieldConfigurations.GroupBy(i => i.FieldName, FileHeaderConfiguration.StringComparer)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key).ToArray();

            if (dupFields.Length > 0)
                throw new ChoRecordConfigurationException("Duplicate field names [Name: {0}] specified to record fields.".FormatString(String.Join(",", dupFields)));

            //Find duplicate fields with start index
            ChoFixedLengthRecordFieldConfiguration dupRecConfig = RecordFieldConfigurations.GroupBy(i => i.StartIndex).Where(g => g.Count() > 1).Select(g => g.FirstOrDefault()).FirstOrDefault();
            if (dupRecConfig != null)
                throw new ChoRecordConfigurationException("Found '{0}' duplicate record field with same start index.".FormatString(dupRecConfig.FieldName));

            //Check any overlapping fields specified
            foreach (var f in RecordFieldConfigurations)
            {
                if (f.StartIndex + f.Size.Value > RecordLength)
                    throw new ChoRecordConfigurationException("Found '{0}' record field out of bounds of record length.".FormatString(f.FieldName));
            }

            RecordFieldConfigurationsDict = RecordFieldConfigurations.OrderBy(i => i.StartIndex).Where(i => !i.Name.IsNullOrWhiteSpace()).ToDictionary(i => i.Name);

            //Validate each record field
            foreach (var fieldConfig in RecordFieldConfigurations)
                fieldConfig.Validate(this);

            if (!FileHeaderConfiguration.HasHeaderRecord)
            {
            }
            else
            {
            }
        }

        private Tuple<string, int, int>[] DiscoverColumns(string line)
        {
            List<Tuple<string, int, int>> words = new List<Tuple<string, int, int>>();
            if (!line.IsNullOrEmpty())
            {
                //const string text = "   Test42  a       yxx ";
                var result = new StringBuilder(line.Length);
                int i = 0;
                while (i < line.Length - 1)
                {
                    result.Append(line[i]);
                    if ((line[i] != '-'
                        && line[i] != '.'
                        && line[i] != '/'
                        && line[i] != '\\') && (Char.IsWhiteSpace(line[i]) && !Char.IsWhiteSpace(line[i + 1])
                        //|| char.IsUpper(line[i + 1])
                        //|| !char.IsDigit(line[i]) && char.IsDigit(line[i + 1])
                        ))
                    {
                        if (Char.IsWhiteSpace(line[i]) && Char.IsWhiteSpace(line[i + 1]))
                        {

                        }
                        else
                        {
                            if (!result.ToString().IsNullOrWhiteSpace())
                            {
                                words.Add(new Tuple<string, int, int>(result.ToString(), i - (result.Length - 1), result.Length));
                                result.Clear();
                            }
                        }
                    }

                    i++;
                }
                result.Append(line[line.Length - 1]);
                string word = result.ToString();
                if (!word.IsNullOrWhiteSpace() || words.Count == 0)
                    words.Add(new Tuple<string, int, int>(result.ToString(), i - (result.Length - 1), result.Length));
                else
                {
                    Tuple<string, int, int> tuple = words[words.Count - 1];
                    words.RemoveAt(words.Count - 1);
                    words.Add(new Tuple<string, int, int>(tuple.Item1 + result.ToString(), tuple.Item2, tuple.Item3 + result.Length));
                }
            }
            //foreach (var item in words)
            //    Console.WriteLine(item.Item1 + " " + item.Item2 + " " + item.Item3);
            return words.ToArray();
        }
    }
}