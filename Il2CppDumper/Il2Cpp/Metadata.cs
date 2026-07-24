using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Il2CppDumper
{
    public sealed class Metadata : BinaryStream
    {
        public Il2CppGlobalMetadataHeader header;
        public Il2CppImageDefinition[] imageDefs;
        public Il2CppAssemblyDefinition[] assemblyDefs;
        public Il2CppTypeDefinition[] typeDefs;
        public Il2CppMethodDefinition[] methodDefs;
        public Il2CppParameterDefinition[] parameterDefs;
        public Il2CppFieldDefinition[] fieldDefs;
        private Dictionary<int, Il2CppFieldDefaultValue> fieldDefaultValuesDic;
        private Dictionary<int, Il2CppParameterDefaultValue> parameterDefaultValuesDic;
        public Il2CppPropertyDefinition[] propertyDefs;
        public Il2CppCustomAttributeTypeRange[] attributeTypeRanges;
        public Il2CppCustomAttributeDataRange[] attributeDataRanges;
        private Dictionary<Il2CppImageDefinition, Dictionary<uint, int>> attributeTypeRangesDic;
        public Il2CppStringLiteral[] stringLiterals;
        private readonly Il2CppMetadataUsageList[] metadataUsageLists;
        private readonly Il2CppMetadataUsagePair[] metadataUsagePairs;
        public int[] attributeTypes;
        public int[] interfaceIndices;
        public Dictionary<Il2CppMetadataUsage, SortedDictionary<uint, uint>> metadataUsageDic;
        public long metadataUsagesCount;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        public int[] constraintIndices;
        public uint[] vtableMethods;
        public Il2CppRGCTXDefinition[] rgctxEntries;

        public ulong ImageBase;

        private readonly Dictionary<uint, string> stringCache = new();

        // v38+ section metadata
        private Il2CppSectionMetadata[] sections;
        private int typeIndexSize;
        private int typeDefIndexSize;
        private int genericContainerIndexSize;
        private int parameterIndexSize;

        private enum SectionIndex
        {
            StringLiterals = 0,
            StringLiteralData,
            Strings,
            Events,
            Properties,
            Methods,
            ParameterDefaultValues,
            FieldDefaultValues,
            FieldAndParameterDefaultValueData,
            FieldMarshaledSizes,
            Parameters,
            Fields,
            GenericParameters,
            GenericParameterConstraints,
            GenericContainers,
            NestedTypes,
            Interfaces,
            VtableMethods,
            InterfaceOffsets,
            TypeDefinitions,
            Images,
            Assemblies,
            FieldRefs,
            ReferencedAssemblies,
            AttributeData,
            AttributeDataRanges,
            UnresolvedIndirectCallParameterTypes,
            UnresolvedIndirectCallParameterRanges,
            WindowsRuntimeTypeNames,
            WindowsRuntimeStrings,
            ExportedTypeDefinitions,
            Count
        }

        public Metadata(Stream stream) : base(stream)
        {
            var sanity = ReadUInt32();
            if (sanity != 0xFAB11BAF)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            var version = ReadInt32();
            if (version < 0 || version > 1000)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            if (version < 16 || (version > 31 && version < 38) || version > 39)
            {
                throw new NotSupportedException($"ERROR: Metadata file supplied is not a supported version[{version}].");
            }
            Version = version;

            if (version >= 38)
            {
                ReadV38(version);
                return;
            }

            header = ReadClass<Il2CppGlobalMetadataHeader>(0);
            if (version == 24)
            {
                if (header.stringLiteralOffset == 264)
                {
                    Version = 24.2;
                    header = ReadClass<Il2CppGlobalMetadataHeader>(0);
                }
                else
                {
                    imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesSize);
                    if (imageDefs.Any(x => x.token != 1))
                    {
                        Version = 24.1;
                    }
                }
            }
            imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesSize);
            if (Version == 24.2 && header.assembliesSize / 68 < imageDefs.Length)
            {
                Version = 24.4;
            }
            var v241Plus = false;
            if (Version == 24.1 && header.assembliesSize / 64 == imageDefs.Length)
            {
                v241Plus = true;
            }
            if (v241Plus)
            {
                Version = 24.4;
            }
            assemblyDefs = ReadMetadataClassArray<Il2CppAssemblyDefinition>(header.assembliesOffset, header.assembliesSize);
            if (v241Plus)
            {
                Version = 24.1;
            }
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(header.typeDefinitionsOffset, header.typeDefinitionsSize);
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(header.methodsOffset, header.methodsSize);
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(header.parametersOffset, header.parametersSize);
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(header.fieldsOffset, header.fieldsSize);
            var fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(header.fieldDefaultValuesOffset, header.fieldDefaultValuesSize);
            var parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(header.parameterDefaultValuesOffset, header.parameterDefaultValuesSize);
            fieldDefaultValuesDic = fieldDefaultValues.ToDictionary(x => x.fieldIndex);
            parameterDefaultValuesDic = parameterDefaultValues.ToDictionary(x => x.parameterIndex);
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(header.propertiesOffset, header.propertiesSize);
            interfaceIndices = ReadClassArray<int>(header.interfacesOffset, header.interfacesSize / 4);
            nestedTypeIndices = ReadClassArray<int>(header.nestedTypesOffset, header.nestedTypesSize / 4);
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(header.eventsOffset, header.eventsSize);
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(header.genericContainersOffset, header.genericContainersSize);
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(header.genericParametersOffset, header.genericParametersSize);
            constraintIndices = ReadClassArray<int>(header.genericParameterConstraintsOffset, header.genericParameterConstraintsSize / 4);
            vtableMethods = ReadClassArray<uint>(header.vtableMethodsOffset, header.vtableMethodsSize / 4);
            stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(header.stringLiteralOffset, header.stringLiteralSize);
            if (Version > 16)
            {
                fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(header.fieldRefsOffset, header.fieldRefsSize);
                if (Version < 27)
                {
                    metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(header.metadataUsageListsOffset, header.metadataUsageListsCount);
                    metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(header.metadataUsagePairsOffset, header.metadataUsagePairsCount);

                    ProcessingMetadataUsage();
                }
            }
            if (Version > 20 && Version < 29)
            {
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(header.attributesInfoOffset, header.attributesInfoCount);
                attributeTypes = ReadClassArray<int>(header.attributeTypesOffset, header.attributeTypesCount / 4);
            }
            if (Version >= 29)
            {
                attributeDataRanges = ReadMetadataClassArray<Il2CppCustomAttributeDataRange>(header.attributeDataRangeOffset, header.attributeDataRangeSize);
            }
            if (Version > 24)
            {
                attributeTypeRangesDic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>();
                foreach (var imageDef in imageDefs)
                {
                    var dic = new Dictionary<uint, int>();
                    attributeTypeRangesDic[imageDef] = dic;
                    var end = imageDef.customAttributeStart + imageDef.customAttributeCount;
                    for (int i = imageDef.customAttributeStart; i < end; i++)
                    {
                        if (Version >= 29)
                        {
                            dic.Add(attributeDataRanges[i].token, i);
                        }
                        else
                        {
                            dic.Add(attributeTypeRanges[i].token, i);
                        }
                    }
                }
            }
            if (Version <= 24.1)
            {
                rgctxEntries = ReadMetadataClassArray<Il2CppRGCTXDefinition>(header.rgctxEntriesOffset, header.rgctxEntriesCount);
            }
        }

        private void ReadV38(int version)
        {
            // Read all section metadata triples from header
            Position = 8;
            var sectionCount = (int)SectionIndex.Count;
            sections = new Il2CppSectionMetadata[sectionCount];
            for (int i = 0; i < sectionCount; i++)
            {
                sections[i] = new Il2CppSectionMetadata
                {
                    offset = ReadInt32(),
                    sectionSize = ReadInt32(),
                    count = ReadInt32()
                };
            }

            // Build a compatible header for downstream code
            header = new Il2CppGlobalMetadataHeader();
            header.sanity = 0xFAB11BAF;
            header.version = version;
            header.stringLiteralOffset = (uint)Sec(SectionIndex.StringLiterals).offset;
            header.stringLiteralSize = Sec(SectionIndex.StringLiterals).sectionSize;
            header.stringLiteralDataOffset = (uint)Sec(SectionIndex.StringLiteralData).offset;
            header.stringLiteralDataSize = Sec(SectionIndex.StringLiteralData).sectionSize;
            header.stringOffset = (uint)Sec(SectionIndex.Strings).offset;
            header.stringSize = Sec(SectionIndex.Strings).sectionSize;
            header.eventsOffset = (uint)Sec(SectionIndex.Events).offset;
            header.eventsSize = Sec(SectionIndex.Events).sectionSize;
            header.propertiesOffset = (uint)Sec(SectionIndex.Properties).offset;
            header.propertiesSize = Sec(SectionIndex.Properties).sectionSize;
            header.methodsOffset = (uint)Sec(SectionIndex.Methods).offset;
            header.methodsSize = Sec(SectionIndex.Methods).sectionSize;
            header.parameterDefaultValuesOffset = (uint)Sec(SectionIndex.ParameterDefaultValues).offset;
            header.parameterDefaultValuesSize = Sec(SectionIndex.ParameterDefaultValues).sectionSize;
            header.fieldDefaultValuesOffset = (uint)Sec(SectionIndex.FieldDefaultValues).offset;
            header.fieldDefaultValuesSize = Sec(SectionIndex.FieldDefaultValues).sectionSize;
            header.fieldAndParameterDefaultValueDataOffset = (uint)Sec(SectionIndex.FieldAndParameterDefaultValueData).offset;
            header.fieldAndParameterDefaultValueDataSize = Sec(SectionIndex.FieldAndParameterDefaultValueData).sectionSize;
            header.fieldMarshaledSizesOffset = Sec(SectionIndex.FieldMarshaledSizes).offset;
            header.fieldMarshaledSizesSize = Sec(SectionIndex.FieldMarshaledSizes).sectionSize;
            header.parametersOffset = (uint)Sec(SectionIndex.Parameters).offset;
            header.parametersSize = Sec(SectionIndex.Parameters).sectionSize;
            header.fieldsOffset = (uint)Sec(SectionIndex.Fields).offset;
            header.fieldsSize = Sec(SectionIndex.Fields).sectionSize;
            header.genericParametersOffset = (uint)Sec(SectionIndex.GenericParameters).offset;
            header.genericParametersSize = Sec(SectionIndex.GenericParameters).sectionSize;
            header.genericParameterConstraintsOffset = (uint)Sec(SectionIndex.GenericParameterConstraints).offset;
            header.genericParameterConstraintsSize = Sec(SectionIndex.GenericParameterConstraints).sectionSize;
            header.genericContainersOffset = (uint)Sec(SectionIndex.GenericContainers).offset;
            header.genericContainersSize = Sec(SectionIndex.GenericContainers).sectionSize;
            header.nestedTypesOffset = (uint)Sec(SectionIndex.NestedTypes).offset;
            header.nestedTypesSize = Sec(SectionIndex.NestedTypes).sectionSize;
            header.interfacesOffset = (uint)Sec(SectionIndex.Interfaces).offset;
            header.interfacesSize = Sec(SectionIndex.Interfaces).sectionSize;
            header.vtableMethodsOffset = (uint)Sec(SectionIndex.VtableMethods).offset;
            header.vtableMethodsSize = Sec(SectionIndex.VtableMethods).sectionSize;
            header.interfaceOffsetsOffset = Sec(SectionIndex.InterfaceOffsets).offset;
            header.interfaceOffsetsSize = Sec(SectionIndex.InterfaceOffsets).sectionSize;
            header.typeDefinitionsOffset = (uint)Sec(SectionIndex.TypeDefinitions).offset;
            header.typeDefinitionsSize = Sec(SectionIndex.TypeDefinitions).sectionSize;
            header.imagesOffset = (uint)Sec(SectionIndex.Images).offset;
            header.imagesSize = Sec(SectionIndex.Images).sectionSize;
            header.assembliesOffset = (uint)Sec(SectionIndex.Assemblies).offset;
            header.assembliesSize = Sec(SectionIndex.Assemblies).sectionSize;
            header.fieldRefsOffset = (uint)Sec(SectionIndex.FieldRefs).offset;
            header.fieldRefsSize = Sec(SectionIndex.FieldRefs).sectionSize;
            header.referencedAssembliesOffset = Sec(SectionIndex.ReferencedAssemblies).offset;
            header.referencedAssembliesSize = Sec(SectionIndex.ReferencedAssemblies).sectionSize;
            header.attributeDataOffset = (uint)Sec(SectionIndex.AttributeData).offset;
            header.attributeDataSize = Sec(SectionIndex.AttributeData).sectionSize;
            header.attributeDataRangeOffset = (uint)Sec(SectionIndex.AttributeDataRanges).offset;
            header.attributeDataRangeSize = Sec(SectionIndex.AttributeDataRanges).sectionSize;

            // Determine dynamic index sizes
            typeDefIndexSize = GetIndexSize(Sec(SectionIndex.TypeDefinitions).count);
            genericContainerIndexSize = GetIndexSize(Sec(SectionIndex.GenericContainers).count);
            parameterIndexSize = version >= 39 ? GetIndexSize(Sec(SectionIndex.Parameters).count) : 4;

            // Determine TypeIndex size from InterfaceOffsets
            var ifOffsets = Sec(SectionIndex.InterfaceOffsets);
            if (ifOffsets.count > 0)
            {
                var actualEntrySize = ifOffsets.sectionSize / ifOffsets.count;
                // Il2CppInterfaceOffsetPair = {TypeIndex, int offset}
                // With TypeIndex=int(4): maxSize = 4 + 4 = 8
                // If actualEntrySize == 8: TypeIndex = 4
                // If actualEntrySize == 6: TypeIndex = 2
                // If actualEntrySize == 5: TypeIndex = 1
                typeIndexSize = actualEntrySize - 4;
                if (typeIndexSize != 1 && typeIndexSize != 2 && typeIndexSize != 4)
                    typeIndexSize = 4;
            }
            else
            {
                typeIndexSize = 4;
            }

            Console.WriteLine($"Dynamic index sizes - TypeIndex:{typeIndexSize} TypeDefIndex:{typeDefIndexSize} GenericContainerIndex:{genericContainerIndexSize} ParameterIndex:{parameterIndexSize}");

            // Read all metadata arrays using dynamic-index-aware readers
            imageDefs = ReadV38Array(SectionIndex.Images, ReadImageDefinitionV38);
            assemblyDefs = ReadClassArray<Il2CppAssemblyDefinition>((uint)Sec(SectionIndex.Assemblies).offset, Sec(SectionIndex.Assemblies).count);
            typeDefs = ReadV38Array(SectionIndex.TypeDefinitions, ReadTypeDefinitionV38);
            methodDefs = ReadV38Array(SectionIndex.Methods, ReadMethodDefinitionV38);
            parameterDefs = ReadV38Array(SectionIndex.Parameters, ReadParameterDefinitionV38);
            fieldDefs = ReadV38Array(SectionIndex.Fields, ReadFieldDefinitionV38);
            var fieldDefaultValues = ReadV38Array(SectionIndex.FieldDefaultValues, ReadFieldDefaultValueV38);
            var parameterDefaultValues = ReadV38Array(SectionIndex.ParameterDefaultValues, ReadParameterDefaultValueV38);
            fieldDefaultValuesDic = fieldDefaultValues.ToDictionary(x => x.fieldIndex);
            parameterDefaultValuesDic = parameterDefaultValues.ToDictionary(x => x.parameterIndex);
            propertyDefs = ReadClassArray<Il2CppPropertyDefinition>((uint)Sec(SectionIndex.Properties).offset, Sec(SectionIndex.Properties).count);
            eventDefs = ReadV38Array(SectionIndex.Events, ReadEventDefinitionV38);
            genericContainers = ReadClassArray<Il2CppGenericContainer>((uint)Sec(SectionIndex.GenericContainers).offset, Sec(SectionIndex.GenericContainers).count);
            genericParameters = ReadV38Array(SectionIndex.GenericParameters, ReadGenericParameterV38);
            fieldRefs = ReadV38Array(SectionIndex.FieldRefs, ReadFieldRefV38);

            // Interface indices are TypeIndex (dynamic width)
            interfaceIndices = ReadDynamicIndexArray((uint)Sec(SectionIndex.Interfaces).offset, Sec(SectionIndex.Interfaces).count, typeIndexSize);
            nestedTypeIndices = ReadClassArray<int>((uint)Sec(SectionIndex.NestedTypes).offset, Sec(SectionIndex.NestedTypes).count);
            constraintIndices = ReadDynamicIndexArray((uint)Sec(SectionIndex.GenericParameterConstraints).offset, Sec(SectionIndex.GenericParameterConstraints).count, typeIndexSize);
            vtableMethods = ReadClassArray<uint>((uint)Sec(SectionIndex.VtableMethods).offset, Sec(SectionIndex.VtableMethods).count);

            // String literals (v35+: no length field, just dataIndex)
            stringLiterals = ReadClassArray<Il2CppStringLiteral>((uint)Sec(SectionIndex.StringLiterals).offset, Sec(SectionIndex.StringLiterals).count);

            // Attribute data ranges (v29+)
            attributeDataRanges = ReadClassArray<Il2CppCustomAttributeDataRange>((uint)Sec(SectionIndex.AttributeDataRanges).offset, Sec(SectionIndex.AttributeDataRanges).count);

            // Fix elementTypeIndex for enums: find the value__ field's typeIndex
            foreach (var td in typeDefs)
            {
                if (td.IsEnum && td.field_count > 0)
                {
                    for (int f = td.fieldStart; f < td.fieldStart + td.field_count; f++)
                    {
                        if (f >= fieldDefs.Length) break;
                        if (GetStringFromIndex(fieldDefs[f].nameIndex) == "value__")
                        {
                            td.elementTypeIndex = fieldDefs[f].typeIndex;
                            break;
                        }
                    }
                }
            }

            // Build attribute type ranges dictionary
            attributeTypeRangesDic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>();
            foreach (var imageDef in imageDefs)
            {
                var dic = new Dictionary<uint, int>();
                attributeTypeRangesDic[imageDef] = dic;
                var end = imageDef.customAttributeStart + imageDef.customAttributeCount;
                for (int i = imageDef.customAttributeStart; i < end; i++)
                {
                    dic.Add(attributeDataRanges[i].token, i);
                }
            }
        }

        private Il2CppSectionMetadata Sec(SectionIndex idx) => sections[(int)idx];

        private static int GetIndexSize(int count)
        {
            if (count <= byte.MaxValue) return 1;
            if (count <= ushort.MaxValue) return 2;
            return 4;
        }

        private int ReadDynamicIndex(int size)
        {
            switch (size)
            {
                case 1:
                    var b = ReadByte();
                    return b == 0xFF ? -1 : b;
                case 2:
                    var s = ReadUInt16();
                    return s == 0xFFFF ? -1 : s;
                default:
                    return ReadInt32();
            }
        }

        private int[] ReadDynamicIndexArray(uint offset, int count, int indexSize)
        {
            Position = offset;
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ReadDynamicIndex(indexSize);
            }
            return result;
        }

        private T[] ReadV38Array<T>(SectionIndex section, Func<T> reader)
        {
            var sec = Sec(section);
            Position = (uint)sec.offset;
            var result = new T[sec.count];
            for (int i = 0; i < sec.count; i++)
            {
                result[i] = reader();
            }
            return result;
        }

        private Il2CppTypeDefinition ReadTypeDefinitionV38()
        {
            var td = new Il2CppTypeDefinition();
            td.nameIndex = ReadUInt32();
            td.namespaceIndex = ReadUInt32();
            td.byvalTypeIndex = ReadDynamicIndex(typeIndexSize);
            td.declaringTypeIndex = ReadDynamicIndex(typeIndexSize);
            td.parentIndex = ReadDynamicIndex(typeIndexSize);
            td.genericContainerIndex = ReadDynamicIndex(genericContainerIndexSize);
            td.flags = ReadUInt32();
            td.fieldStart = ReadInt32();
            td.methodStart = ReadInt32();
            td.eventStart = ReadInt32();
            td.propertyStart = ReadInt32();
            td.nestedTypesStart = ReadInt32();
            td.interfacesStart = ReadInt32();
            td.vtableStart = ReadInt32();
            td.interfaceOffsetsStart = ReadInt32();
            td.method_count = ReadUInt16();
            td.property_count = ReadUInt16();
            td.field_count = ReadUInt16();
            td.event_count = ReadUInt16();
            td.nested_type_count = ReadUInt16();
            td.vtable_count = ReadUInt16();
            td.interfaces_count = ReadUInt16();
            td.interface_offsets_count = ReadUInt16();
            td.bitfield = ReadUInt32();
            td.token = ReadUInt32();
            return td;
        }

        private Il2CppMethodDefinition ReadMethodDefinitionV38()
        {
            var md = new Il2CppMethodDefinition();
            md.nameIndex = ReadUInt32();
            md.declaringType = ReadDynamicIndex(typeDefIndexSize);
            md.returnType = ReadDynamicIndex(typeIndexSize);
            md.returnParameterToken = ReadInt32();
            md.parameterStart = ReadDynamicIndex(parameterIndexSize);
            md.genericContainerIndex = ReadDynamicIndex(genericContainerIndexSize);
            md.token = ReadUInt32();
            md.flags = ReadUInt16();
            md.iflags = ReadUInt16();
            md.slot = ReadUInt16();
            md.parameterCount = ReadUInt16();
            return md;
        }

        private Il2CppParameterDefinition ReadParameterDefinitionV38()
        {
            var pd = new Il2CppParameterDefinition();
            pd.nameIndex = ReadUInt32();
            pd.token = ReadUInt32();
            pd.typeIndex = ReadDynamicIndex(typeIndexSize);
            return pd;
        }

        private Il2CppFieldDefinition ReadFieldDefinitionV38()
        {
            var fd = new Il2CppFieldDefinition();
            fd.nameIndex = ReadUInt32();
            fd.typeIndex = ReadDynamicIndex(typeIndexSize);
            fd.token = ReadUInt32();
            return fd;
        }

        private Il2CppEventDefinition ReadEventDefinitionV38()
        {
            var ed = new Il2CppEventDefinition();
            ed.nameIndex = ReadUInt32();
            ed.typeIndex = ReadDynamicIndex(typeIndexSize);
            ed.add = ReadInt32();
            ed.remove = ReadInt32();
            ed.raise = ReadInt32();
            ed.token = ReadUInt32();
            return ed;
        }

        private Il2CppGenericParameter ReadGenericParameterV38()
        {
            var gp = new Il2CppGenericParameter();
            gp.ownerIndex = ReadDynamicIndex(genericContainerIndexSize);
            gp.nameIndex = ReadUInt32();
            gp.constraintsStart = ReadInt16();
            gp.constraintsCount = ReadInt16();
            gp.num = ReadUInt16();
            gp.flags = ReadUInt16();
            return gp;
        }

        private Il2CppFieldRef ReadFieldRefV38()
        {
            var fr = new Il2CppFieldRef();
            fr.typeIndex = ReadDynamicIndex(typeIndexSize);
            fr.fieldIndex = ReadInt32();
            return fr;
        }

        private Il2CppImageDefinition ReadImageDefinitionV38()
        {
            var img = new Il2CppImageDefinition();
            img.nameIndex = ReadUInt32();
            img.assemblyIndex = ReadInt32();
            img.typeStart = ReadDynamicIndex(typeDefIndexSize);
            img.typeCount = ReadUInt32();
            img.exportedTypeStart = ReadDynamicIndex(typeDefIndexSize);
            img.exportedTypeCount = ReadUInt32();
            img.entryPointIndex = ReadInt32();
            img.token = ReadUInt32();
            img.customAttributeStart = ReadInt32();
            img.customAttributeCount = ReadUInt32();
            return img;
        }

        private Il2CppFieldDefaultValue ReadFieldDefaultValueV38()
        {
            var fdv = new Il2CppFieldDefaultValue();
            fdv.fieldIndex = ReadInt32();
            fdv.typeIndex = ReadDynamicIndex(typeIndexSize);
            fdv.dataIndex = ReadInt32();
            return fdv;
        }

        private Il2CppParameterDefaultValue ReadParameterDefaultValueV38()
        {
            var pdv = new Il2CppParameterDefaultValue();
            pdv.parameterIndex = ReadDynamicIndex(parameterIndexSize);
            pdv.typeIndex = ReadDynamicIndex(typeIndexSize);
            pdv.dataIndex = ReadInt32();
            return pdv;
        }

        private T[] ReadMetadataClassArray<T>(uint addr, int count) where T : new()
        {
            return ReadClassArray<T>(addr, count / SizeOf(typeof(T)));
        }

        public bool GetFieldDefaultValueFromIndex(int index, out Il2CppFieldDefaultValue value)
        {
            return fieldDefaultValuesDic.TryGetValue(index, out value);
        }

        public bool GetParameterDefaultValueFromIndex(int index, out Il2CppParameterDefaultValue value)
        {
            return parameterDefaultValuesDic.TryGetValue(index, out value);
        }

        public uint GetDefaultValueFromIndex(int index)
        {
            return (uint)(header.fieldAndParameterDefaultValueDataOffset + index);
        }

        public string GetStringFromIndex(uint index)
        {
            if (!stringCache.TryGetValue(index, out var result))
            {
                result = ReadStringToNull(header.stringOffset + index);
                stringCache.Add(index, result);
            }
            return result;
        }

        public int GetCustomAttributeIndex(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token)
        {
            if (Version > 24)
            {
                if (attributeTypeRangesDic[imageDef].TryGetValue(token, out var index))
                {
                    return index;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return customAttributeIndex;
            }
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            var stringLiteral = stringLiterals[index];
            if (Version >= 38)
            {
                // v38+: length field removed; computed from adjacent entries' dataIndex
                int dataOffset = (int)header.stringLiteralDataOffset;
                int currentDataIndex = stringLiteral.dataIndex;
                int length;
                if (index + 1 < stringLiterals.Length)
                {
                    length = stringLiterals[index + 1].dataIndex - currentDataIndex;
                }
                else
                {
                    length = Sec(SectionIndex.StringLiteralData).sectionSize - currentDataIndex;
                }
                Position = (uint)(dataOffset + currentDataIndex);
                return Encoding.UTF8.GetString(ReadBytes(length));
            }
            else
            {
                Position = (uint)(header.stringLiteralDataOffset + stringLiteral.dataIndex);
                return Encoding.UTF8.GetString(ReadBytes((int)stringLiteral.length));
            }
        }

        private void ProcessingMetadataUsage()
        {
            metadataUsageDic = new Dictionary<Il2CppMetadataUsage, SortedDictionary<uint, uint>>();
            for (uint i = 1; i <= 6; i++)
            {
                metadataUsageDic[(Il2CppMetadataUsage)i] = new SortedDictionary<uint, uint>();
            }
            foreach (var metadataUsageList in metadataUsageLists)
            {
                for (int i = 0; i < metadataUsageList.count; i++)
                {
                    var offset = metadataUsageList.start + i;
                    if (offset >= metadataUsagePairs.Length)
                    {
                        continue;
                    }
                    var metadataUsagePair = metadataUsagePairs[offset];
                    var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                    var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                    metadataUsageDic[(Il2CppMetadataUsage)usage][metadataUsagePair.destinationIndex] = decodedIndex;
                }
            }
            metadataUsagesCount = metadataUsageDic.Max(x => x.Value.Select(y => y.Key).DefaultIfEmpty().Max()) + 1;
        }

        public static uint GetEncodedIndexType(uint index)
        {
            return (index & 0xE0000000) >> 29;
        }

        public uint GetDecodedMethodIndex(uint index)
        {
            if (Version >= 27)
            {
                return (index & 0x1FFFFFFEU) >> 1;
            }
            return index & 0x1FFFFFFFU;
        }

        public int SizeOf(Type type)
        {
            var size = 0;
            foreach (var i in type.GetFields())
            {
                var attr = (VersionAttribute)Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                if (attr != null)
                {
                    if (Version < attr.Min || Version > attr.Max)
                        continue;
                }
                var fieldType = i.FieldType;
                if (fieldType.IsPrimitive)
                {
                    size += GetPrimitiveTypeSize(fieldType.Name);
                }
                else if (fieldType.IsEnum)
                {
                    var e = fieldType.GetField("value__").FieldType;
                    size += GetPrimitiveTypeSize(e.Name);
                }
                else if (fieldType.IsArray)
                {
                    var arrayLengthAttribute = i.GetCustomAttribute<ArrayLengthAttribute>();
                    size += arrayLengthAttribute.Length;
                }
                else
                {
                    size += SizeOf(fieldType);
                }
            }
            return size;

            static int GetPrimitiveTypeSize(string name)
            {
                return name switch
                {
                    "Int32" or "UInt32" => 4,
                    "Int16" or "UInt16" => 2,
                    _ => 0,
                };
            }
        }
    }
}
