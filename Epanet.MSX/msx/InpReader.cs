/*
 * Copyright (C) 2016 Vyacheslav Shevelyov (slavash at aha dot ru)
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see http://www.gnu.org/licenses/.
 */

using System;
using System.Diagnostics;
using System.IO;
using Epanet.MSX.Structures;
using Epanet.Util;

namespace Epanet.MSX {

    public class InpReader {

        private TraceSource log;
        private const int MAXERRS = 100; // Max. input errors reported

        private Network _msx;
        private EnToolkit2 _epanet;
        private Project _project;

        public void LoadDependencies(EpanetMSX epa) {
            _msx = epa.Network;
            _epanet = epa.EnToolkit;
            _project = epa.Project;
        }


        /// <summary>Respective error messages.</summary>
        private static readonly string[] inpErrorTxt = {
            "",
            "Error 401 (too many characters)",
            "Error 402 (too few input items)",
            "Error 403 (invalid keyword)",
            "Error 404 (invalid numeric value)",
            "Error 405 (reference to undefined object)",
            "Error 406 (illegal use of a reserved name)",
            "Error 407 (name already used by another object)",
            "Error 408 (species already assigned an expression)",
            "Error 409 (illegal math expression)"
        };

        /// <summary>Reads multi-species input file to determine number of system objects.</summary>
        public ErrorCodeType CountMsxObjects(TextReader reader) {
            SectionType sect = (SectionType)(-1); // input data sections
            InpErrorCodes errcode = 0; // error code
            int errsum = 0; // number of errors found
            long lineCount = 0;


            //BufferedReader reader = (BufferedReader)MSX.MsxFile.getFileIO();

            for (;;) {
                string line; // line from input data file
                try {
                    line = reader.ReadLine();
                }
                catch (IOException) {
                    break;
                }

                if (line == null)
                    break;

                errcode = 0;
                line = line.Trim();
                lineCount++;

                int comentPosition = line.IndexOf(';');
                if (comentPosition != -1)
                    line = line.Substring(0, comentPosition);

                if (string.IsNullOrEmpty(line))
                    continue;

                string[] tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

                if (tok.Length == 0 || !string.IsNullOrEmpty(tok[0]) && tok[0][0] == ';') continue;

                SectionType sectTemp;
                if (GetNewSection(tok[0], Constants.MsxSectWords, out sectTemp) != 0) {
                    sect = sectTemp;
                    continue;
                }

                if (sect == SectionType.s_SPECIES)
                    errcode = AddSpecies(tok);
                if (sect == SectionType.s_COEFF)
                    errcode = AddCoeff(tok);
                if (sect == SectionType.s_TERM)
                    errcode = AddTerm(tok);
                if (sect == SectionType.s_PATTERN)
                    errcode = AddPattern(tok);


                if (errcode != 0) {
                    WriteInpErrMsg(errcode, Constants.MsxSectWords[(int)sect], line, (int)lineCount);
                    errsum++;
                    if (errsum >= MAXERRS) break;
                }
            }

            //return error code

            if (errsum > 0) return ErrorCodeType.ERR_MSX_INPUT;
            return (ErrorCodeType)errcode;
        }

        /// <summary>Queries EPANET database to determine number of network objects.</summary>
        public ErrorCodeType CountNetObjects() {
            _msx.Nobjects[(int)ObjectTypes.NODE] = _epanet.ENgetcount(EnToolkit2.EN_NODECOUNT);
            _msx.Nobjects[(int)ObjectTypes.TANK] = _epanet.ENgetcount(EnToolkit2.EN_TANKCOUNT);
            _msx.Nobjects[(int)ObjectTypes.LINK] = _epanet.ENgetcount(EnToolkit2.EN_LINKCOUNT);
            return 0;
        }

        /// <summary>Retrieves required input data from the EPANET project data.</summary>
        public ErrorCodeType ReadNetData() {
            // Get flow units & time parameters
            _msx.Flowflag = _epanet.ENgetflowunits();

            _msx.Unitsflag = _msx.Flowflag >= FlowUnitsType.LPS
                ? UnitSystemType.SI
                : UnitSystemType.US;

            _msx.Dur = _epanet.ENgettimeparam(EnToolkit2.EN_DURATION);
            _msx.Qstep = _epanet.ENgettimeparam(EnToolkit2.EN_QUALSTEP);
            _msx.Rstep = _epanet.ENgettimeparam(EnToolkit2.EN_REPORTSTEP);
            _msx.Rstart = _epanet.ENgettimeparam(EnToolkit2.EN_REPORTSTART);
            _msx.Pstep = _epanet.ENgettimeparam(EnToolkit2.EN_PATTERNSTEP);
            _msx.Pstart = _epanet.ENgettimeparam(EnToolkit2.EN_PATTERNSTART);
            _msx.Statflag = (TstatType)_epanet.ENgettimeparam(EnToolkit2.EN_STATISTIC);

            // Read tank/reservoir data
            int n = _msx.Nobjects[(int)ObjectTypes.NODE] - _msx.Nobjects[(int)ObjectTypes.TANK];
            for (int i = 1; i <= _msx.Nobjects[(int)ObjectTypes.NODE]; i++) {
                int k = i - n;
                if (k <= 0) continue;

                int t;
                float v0;
                float xmix;
                float vmix;

                try {
                    t = _epanet.ENgetnodetype(i);
                    v0 = _epanet.ENgetnodevalue(i, EnToolkit2.EN_INITVOLUME);
                    xmix = _epanet.ENgetnodevalue(i, EnToolkit2.EN_MIXMODEL);
                    vmix = _epanet.ENgetnodevalue(i, EnToolkit2.EN_MIXZONEVOL);
                }
                catch (Exception e) {
                    return (ErrorCodeType)int.Parse(e.Message);
                }

                _msx.Node[i].Tank = k;
                _msx.Tank[k].Node = i;
                _msx.Tank[k].A = t == EnToolkit2.EN_RESERVOIR ? 0.0 : 1.0;
                _msx.Tank[k].V0 = v0;
                _msx.Tank[k].MixModel = (MixType)(int)xmix;
                _msx.Tank[k].VMix = vmix;
            }

            // Read link data
            for (int i = 1; i <= _msx.Nobjects[(int)ObjectTypes.LINK]; i++) {
                int n1, n2;

                try {
                    _epanet.ENgetlinknodes(i, out n1, out n2);
                }
                catch (Exception e) {
                    return (ErrorCodeType)int.Parse(e.Message);
                }

                
                float roughness;
                float diam;
                float len;
                try {
                    diam = _epanet.ENgetlinkvalue(i, EnToolkit2.EN_DIAMETER);
                    len = _epanet.ENgetlinkvalue(i, EnToolkit2.EN_LENGTH);
                    roughness = _epanet.ENgetlinkvalue(i, EnToolkit2.EN_ROUGHNESS);
                }
                catch (Exception e) {
                    return (ErrorCodeType)int.Parse(e.Message);
                }

                _msx.Link[i].N1 = n1;
                _msx.Link[i].N2 = n2;
                _msx.Link[i].Diam = diam;
                _msx.Link[i].Len = len;
                _msx.Link[i].Roughness = roughness;
            }
            return 0;
        }

        /// <summary>Reads multi-species data from the EPANET-MSX input file.</summary>
        public ErrorCodeType ReadMsxData(TextReader rin) {
            var sect = (SectionType)(-1); // input data sections
            int errsum = 0; // number of errors found
            int lineCount = 0; // line count

            // rewind
            //MSX.MsxFile.close();
            //MSX.MsxFile.openAsTextReader();

            //BufferedReader rin = (BufferedReader)MSX.MsxFile.getFileIO();

            for (;;) {
                string line; // line from input data file
                try {
                    line = rin.ReadLine();
                }
                catch (IOException) {
                    break;
                }

                if (line == null)
                    break;

                lineCount++;
                line = line.Trim();

                int comentPosition = line.IndexOf(';');
                if (comentPosition != -1)
                    line = line.Substring(0, comentPosition);

                if (string.IsNullOrEmpty(line))
                    continue;

                string[] tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

                if (tok.Length == 0) continue;

                InpErrorCodes inperr; // input error code
                if (GetLineLength(line) >= Constants.MAXLINE) {
                    inperr = InpErrorCodes.ERR_LINE_LENGTH;
                    WriteInpErrMsg(inperr, Constants.MsxSectWords[(int)sect], line, lineCount);
                    errsum++;
                }

                SectionType sectTmp;
                if (GetNewSection(tok[0], Constants.MsxSectWords, out sectTmp) != 0) {
                    sect = sectTmp;
                    continue;
                }

                inperr = ParseLine(sect, line, tok);

                if (inperr > 0) {
                    errsum++;
                    WriteInpErrMsg(inperr, Constants.MsxSectWords[(int)sect], line, lineCount);
                }

                // Stop if reach end of file or max. error count
                if (errsum >= MAXERRS) break;
            }

            if (errsum > 0)
                return (ErrorCodeType)200;

            return 0;
        }

        /// <summary>Reads multi-species data from the EPANET-MSX input file.</summary>
        public string MSXinp_getSpeciesUnits(int m) {
            string units = _msx.Species[m].Units;
            units += "/";
            if (_msx.Species[m].Type == SpeciesType.BULK)
                units += "L";
            else
                units += Constants.AreaUnitsWords[(int)_msx.AreaUnits];

            return units;
        }

        /// <summary>Determines number of characters of data in a line of input.</summary>
        private static int GetLineLength(string line) {
            int index = line.IndexOf(';');

            return index != -1 ? line.Substring(0, index).Length : line.Length;
        }

        /// <summary>Checks if a line begins a new section in the input file.</summary>
        private static int GetNewSection(string tok, string[] sectWords, out SectionType sect) {
            sect = (SectionType)(-1);
            if (string.IsNullOrEmpty(tok))
                return 0;
            // --- check if line begins with a new section heading

            if (tok[0] == '[') {
                // --- look for section heading in list of section keywords

                int newsect = Utilities.MSXutils_findmatch(tok, sectWords);
                if (newsect >= 0) sect = (SectionType)newsect;
                else
                    sect = (SectionType)(-1);
                return 1;
            }
            return 0;
        }

        /// <summary>Adds a species ID name to the project.</summary>
        private InpErrorCodes AddSpecies(string[] tok) {
            if (tok.Length < 2) return InpErrorCodes.ERR_ITEMS;
            InpErrorCodes errcode = CheckId(tok[1]);
            if (errcode != 0) return errcode;
            if (_project.MSXproj_addObject(
                        ObjectTypes.SPECIES,
                        tok[1],
                        _msx.Nobjects[(int)ObjectTypes.SPECIES] + 1) < 0)
                errcode = (InpErrorCodes)101;
            else _msx.Nobjects[(int)ObjectTypes.SPECIES]++;
            return errcode;
        }

        /// <summary>Adds a coefficient ID name to the project.</summary>
        private InpErrorCodes AddCoeff(string[] tok) {
            ObjectTypes k;

            // determine the type of coeff.

            if (tok.Length < 2) return InpErrorCodes.ERR_ITEMS;
            if (Utilities.MSXutils_match(tok[0], "PARAM")) k = ObjectTypes.PARAMETER;
            else if (Utilities.MSXutils_match(tok[0], "CONST")) k = ObjectTypes.CONSTANT;
            else return InpErrorCodes.ERR_KEYWORD;

            // check for valid id name

            InpErrorCodes errcode = CheckId(tok[1]);
            if (errcode != 0) return errcode;
            if (_project.MSXproj_addObject(k, tok[1], _msx.Nobjects[(int)k] + 1) < 0)
                errcode = (InpErrorCodes)101;
            else _msx.Nobjects[(int)k]++;
            return errcode;
        }


        /// <summary>Adds an intermediate expression term ID name to the project.</summary>
        private InpErrorCodes AddTerm(string[] id) {
            InpErrorCodes errcode = CheckId(id[0]);
            if (errcode == 0) {
                if (_project.MSXproj_addObject(
                            ObjectTypes.TERM,
                            id[0],
                            _msx.Nobjects[(int)ObjectTypes.TERM] + 1) < 0)
                    errcode = (InpErrorCodes)101;
                else _msx.Nobjects[(int)ObjectTypes.TERM]++;
            }
            return errcode;
        }


        /// <summary>Adds a time pattern ID name to the project.</summary>
        private InpErrorCodes AddPattern(string[] tok) {
            InpErrorCodes errcode = 0;

            // A time pattern can span several lines

            if (_project.MSXproj_findObject(ObjectTypes.PATTERN, tok[0]) <= 0) {
                if (_project.MSXproj_addObject(
                            ObjectTypes.PATTERN,
                            tok[0],
                            _msx.Nobjects[(int)ObjectTypes.PATTERN] + 1) < 0)
                    errcode = (InpErrorCodes)101;
                else _msx.Nobjects[(int)ObjectTypes.PATTERN]++;
            }
            return errcode;
        }


        /// <summary>Checks that an object's name is unique.</summary>
        private InpErrorCodes CheckId(string id) {
            // Check that id name is not a reserved word
            foreach (string word  in  Constants.HydVarWords) {
                if (string.Equals(id, word, StringComparison.OrdinalIgnoreCase)) 
                    return InpErrorCodes.ERR_RESERVED_NAME;
            }

            // Check that id name not used before

            if (_project.MSXproj_findObject(ObjectTypes.SPECIES, id) > 0
                || _project.MSXproj_findObject(ObjectTypes.TERM, id) > 0
                || _project.MSXproj_findObject(ObjectTypes.PARAMETER, id) > 0
                || _project.MSXproj_findObject(ObjectTypes.CONSTANT, id) > 0
            ) return InpErrorCodes.ERR_DUP_NAME;
            return 0;
        }


        /// <summary>Parses the contents of a line of input data.</summary>
        private InpErrorCodes ParseLine(SectionType sect, string line, string[] tok) {
            switch (sect) {
            case SectionType.s_TITLE:
                _msx.Title = line;
                break;

            case SectionType.s_OPTION:
                return ParseOption(tok);

            case SectionType.s_SPECIES:
                return ParseSpecies(tok);

            case SectionType.s_COEFF:
                return ParseCoeff(tok);

            case SectionType.s_TERM:
                return ParseTerm(tok);

            case SectionType.s_PIPE:
                return ParseExpression(ObjectTypes.LINK, tok);

            case SectionType.s_TANK:
                return ParseExpression(ObjectTypes.TANK, tok);

            case SectionType.s_SOURCE:
                return ParseSource(tok);

            case SectionType.s_QUALITY:
                return ParseQuality(tok);

            case SectionType.s_PARAMETER:
                return ParseParameter(tok);

            case SectionType.s_PATTERN:
                return ParsePattern(tok);

            case SectionType.s_REPORT:
                return ParseReport(tok);
            }
            return 0;
        }

        /// <summary>Parses an input line containing a project option.</summary>
        private InpErrorCodes ParseOption(string[] tok) {
            // Determine which option is being read

            if (tok.Length < 2) return 0;
            int k = Utilities.MSXutils_findmatch(tok[0], Constants.OptionTypeWords);
            if (k < 0) return InpErrorCodes.ERR_KEYWORD;

            // Parse the value for the given option
            switch ((OptionType)k) {
            case OptionType.AREA_UNITS_OPTION:
                k = Utilities.MSXutils_findmatch(tok[1], Constants.AreaUnitsWords);
                if (k < 0) return InpErrorCodes.ERR_KEYWORD;
                _msx.AreaUnits = (AreaUnitsType)k;
                break;

            case OptionType.RATE_UNITS_OPTION:
                k = Utilities.MSXutils_findmatch(tok[1], Constants.TimeUnitsWords);
                if (k < 0) return InpErrorCodes.ERR_KEYWORD;
                _msx.RateUnits = (RateUnitsType)k;
                break;

            case OptionType.SOLVER_OPTION:
                k = Utilities.MSXutils_findmatch(tok[1], Constants.SolverTypeWords);
                if (k < 0) return InpErrorCodes.ERR_KEYWORD;
                _msx.Solver = (SolverType)k;
                break;

            case OptionType.COUPLING_OPTION:
                k = Utilities.MSXutils_findmatch(tok[1], Constants.CouplingWords);
                if (k < 0) return InpErrorCodes.ERR_KEYWORD;
                _msx.Coupling = (CouplingType)k;
                break;

            case OptionType.TIMESTEP_OPTION:
                k = int.Parse(tok[1]);
                if (k <= 0) return InpErrorCodes.ERR_NUMBER;
                _msx.Qstep = k;
                break;

            case OptionType.RTOL_OPTION: {
                double tmp;
                if (!tok[1].ToDouble(out tmp)) return InpErrorCodes.ERR_NUMBER;
                _msx.DefRtol = tmp;
                break;
            }
            case OptionType.ATOL_OPTION: {
                double tmp;
                if (!tok[1].ToDouble(out tmp)) return InpErrorCodes.ERR_NUMBER;
                _msx.DefAtol = tmp;
            }
                break;
            }
            return 0;
        }

        /// <summary>Parses an input line containing a species variable.</summary>
        private InpErrorCodes ParseSpecies(string[] tok) {
            // Get secies index
            if (tok.Length < 3) return InpErrorCodes.ERR_ITEMS;
            int i = _project.MSXproj_findObject(ObjectTypes.SPECIES, tok[1]);
            if (i <= 0) return InpErrorCodes.ERR_NAME;

            // Get pointer to Species name
            _msx.Species[i].Id = _project.MSXproj_findID(ObjectTypes.SPECIES, tok[1]);

            // Get species type
            if (Utilities.MSXutils_match(tok[0], "BULK")) _msx.Species[i].Type = SpeciesType.BULK;
            else if (Utilities.MSXutils_match(tok[0], "WALL")) _msx.Species[i].Type = SpeciesType.WALL;
            else return InpErrorCodes.ERR_KEYWORD;

            // Get Species units
            _msx.Species[i].Units = tok[2];

            // Get Species error tolerance
            _msx.Species[i].ATol = 0.0;
            _msx.Species[i].RTol = 0.0;
            if (tok.Length >= 4) {
                double tmp;
                // BUG: Baseform bug
                if (!tok[3].ToDouble(out tmp))
                    _msx.Species[i].ATol = tmp;
                return InpErrorCodes.ERR_NUMBER;
            }
            if (tok.Length >= 5) {
                double tmp;
                // BUG: Baseform bug
                if (!tok[4].ToDouble(out tmp)) //&MSX.Species[i].rTol) )
                    _msx.Species[i].RTol = tmp;
                return InpErrorCodes.ERR_NUMBER;
            }
            return 0;
        }

        /// <summary>Parses an input line containing a coefficient definition.</summary>
        private InpErrorCodes ParseCoeff(string[] tok) {
            
            // Check if variable is a Parameter
            if (tok.Length < 2) return 0;
       
            if (Utilities.MSXutils_match(tok[0], "PARAM")) {
                // Get Parameter's index
                int i = _project.MSXproj_findObject(ObjectTypes.PARAMETER, tok[1]);
                if (i <= 0) return InpErrorCodes.ERR_NAME;

                // Get Parameter's value
                _msx.Param[i].Id = _project.MSXproj_findID(ObjectTypes.PARAMETER, tok[1]);
                if (tok.Length >= 3) {
                    // BUG: Baseform bug
                    double x;
                    if (tok[2].ToDouble(out x)) return InpErrorCodes.ERR_NUMBER;
                    _msx.Param[i].Value = x;
                    
                    for (int j = 1; j <= _msx.Nobjects[(int)ObjectTypes.LINK]; j++)
                        _msx.Link[j].Param[i] = x;
                    for (int j = 1; j <= _msx.Nobjects[(int)ObjectTypes.TANK]; j++)
                        _msx.Tank[j].Param[i] = x;
                }
                return 0;
            }

            // Check if variable is a Constant
            else if (Utilities.MSXutils_match(tok[0], "CONST")) {
                // Get Constant's index
                int i = _project.MSXproj_findObject(ObjectTypes.CONSTANT, tok[1]);
                if (i <= 0) return InpErrorCodes.ERR_NAME;

                // Get constant's value
                _msx.Const[i].Id = _project.MSXproj_findID(ObjectTypes.CONSTANT, tok[1]);
                _msx.Const[i].Value = 0.0;
                if (tok.Length >= 3) {
                    double tmp;
                    if (!tok[2].ToDouble(out tmp)) //&MSX.Const[i].value) )
                        return InpErrorCodes.ERR_NUMBER;
                    _msx.Const[i].Value = tmp;
                }
                return 0;
            }
            else
                return InpErrorCodes.ERR_KEYWORD;
        }

       /// <summary>Parses an input line containing an intermediate expression term .</summary>
        private InpErrorCodes ParseTerm(string[] tok) {
            string s = "";

           // --- get term's name

            if (tok.Length < 2) return 0;
            int i = _project.MSXproj_findObject(ObjectTypes.TERM, tok[0]);

            // --- reconstruct the expression string from its tokens

            for (int j = 1; j < tok.Length; j++) s += tok[j];

            // --- convert expression into a postfix stack of op codes

            //expr = mathexpr_create(s, getVariableCode);
            MathExpr expr = MathExpr.Create(s, GetVariableCode);
            if (expr == null) return InpErrorCodes.ERR_MATH_EXPR;

            // --- assign the expression to a Term object

            _msx.Term[i].Expr = expr;
            return 0;
        }

        /// <summary>Parses an input line containing a math expression.</summary>
        private InpErrorCodes ParseExpression(ObjectTypes classType, string[] tok) {
            string s = "";

            // --- determine expression type

            if (tok.Length < 3) return InpErrorCodes.ERR_ITEMS;
            int k = Utilities.MSXutils_findmatch(tok[0], Constants.ExprTypeWords);
            if (k < 0) return InpErrorCodes.ERR_KEYWORD;

            // --- determine species associated with expression

            int i = _project.MSXproj_findObject(ObjectTypes.SPECIES, tok[1]);
            if (i < 1) return InpErrorCodes.ERR_NAME;

            // --- check that species does not already have an expression

            if (classType == ObjectTypes.LINK) {
                if (_msx.Species[i].PipeExprType != ExpressionType.NO_EXPR)
                    return InpErrorCodes.ERR_DUP_EXPR;
            }

            if (classType == ObjectTypes.TANK) {
                if (_msx.Species[i].TankExprType != ExpressionType.NO_EXPR)
                    return InpErrorCodes.ERR_DUP_EXPR;
            }

            // --- reconstruct the expression string from its tokens

            for (int j = 2; j < tok.Length; j++) s += tok[j];

            // --- convert expression into a postfix stack of op codes

            //expr = mathexpr_create(s, getVariableCode);
            MathExpr expr = MathExpr.Create(s, GetVariableCode);

            if (expr == null) return InpErrorCodes.ERR_MATH_EXPR;

            // --- assign the expression to the species

            switch (classType) {
            case ObjectTypes.LINK:
                _msx.Species[i].PipeExpr = expr;
                _msx.Species[i].PipeExprType = (ExpressionType)k;
                break;
            case ObjectTypes.TANK:
                _msx.Species[i].TankExpr = expr;
                _msx.Species[i].TankExprType = (ExpressionType)k;
                break;
            }
            return 0;
        }

        /// <summary>Parses an input line containing initial species concentrations.</summary>
        private InpErrorCodes ParseQuality(string[] tok) {
            int err, i, j, k, m;
            double x;

            // --- determine if quality value is global or object-specific

            if (tok.Length < 3) return InpErrorCodes.ERR_ITEMS;
            if (Utilities.MSXutils_match(tok[0], "GLOBAL")) i = 1;
            else if (Utilities.MSXutils_match(tok[0], "NODE")) i = 2;
            else if (Utilities.MSXutils_match(tok[0], "LINK")) i = 3;
            else return InpErrorCodes.ERR_KEYWORD;

            // --- find species index

            k = 1;
            if (i >= 2) k = 2;
            m = _project.MSXproj_findObject(ObjectTypes.SPECIES, tok[k]);
            if (m <= 0) return InpErrorCodes.ERR_NAME;

            // --- get quality value

            if (i >= 2 && tok.Length < 4) return InpErrorCodes.ERR_ITEMS;
            k = 2;
            if (i >= 2) k = 3;
            if (!tok[k].ToDouble(out x)) return InpErrorCodes.ERR_NUMBER;

            // --- for global specification, set initial quality either for
            //     all nodes or links depending on type of species

            if (i == 1) {
                _msx.C0[m] = x;
                if (_msx.Species[m].Type == SpeciesType.BULK) {
                    for (j = 1; j <= _msx.Nobjects[(int)ObjectTypes.NODE]; j++)
                        _msx.Node[j].C0[m] = x;
                }
                for (j = 1; j <= _msx.Nobjects[(int)ObjectTypes.LINK]; j++)
                    _msx.Link[j].C0[m] = x;
            }

            // --- for a specific node, get its index & set its initial quality

            else if (i == 2) {
                int tmp;
                err = _epanet.ENgetnodeindex(tok[1], out tmp);
                j = tmp;
                if (err != 0) return InpErrorCodes.ERR_NAME;
                if (_msx.Species[m].Type == SpeciesType.BULK) _msx.Node[j].C0[m] = x;
            }

            // --- for a specific link, get its index & set its initial quality

            else if (i == 3) {
                int tmp;
                err = _epanet.ENgetlinkindex(tok[1], out tmp);
                j = tmp;
                if (err != 0)
                    return InpErrorCodes.ERR_NAME;

                _msx.Link[j].C0[m] = x;
            }
            return 0;
        }

        /// <summary>Parses an input line containing a parameter data.</summary>
        private InpErrorCodes ParseParameter(string[] tok) {
            int err, j;

            // --- get parameter name

            if (tok.Length < 4) return 0;
            int i = _project.MSXproj_findObject(ObjectTypes.PARAMETER, tok[2]);

            // --- get parameter value

            double x;
            if (!tok[3].ToDouble(out x)) return InpErrorCodes.ERR_NUMBER;
            
            // --- for pipe parameter, get pipe index and update parameter's value

            if (Utilities.MSXutils_match(tok[0], "PIPE")) {
                err = _epanet.ENgetlinkindex(tok[1], out j);
              
                if (err != 0) return InpErrorCodes.ERR_NAME;
                _msx.Link[j].Param[i] = x;
            }

            // --- for tank parameter, get tank index and update parameter's value

            else if (Utilities.MSXutils_match(tok[0], "TANK")) {
                err = _epanet.ENgetnodeindex(tok[1], out j);
                if (err != 0) return InpErrorCodes.ERR_NAME;
                j = _msx.Node[j].Tank;
                if (j > 0) _msx.Tank[j].Param[i] = x;
            }
            else return InpErrorCodes.ERR_KEYWORD;
            return 0;
        }

    /// <summary>Parses an input line containing a source input data.</summary>
        private InpErrorCodes ParseSource(string[] tok) {
           
            Source source = null;

            // --- get source type
            if (tok.Length < 4) return InpErrorCodes.ERR_ITEMS;
            int k = Utilities.MSXutils_findmatch(tok[0], Constants.SourceTypeWords);
            if (k < 0) return InpErrorCodes.ERR_KEYWORD;

            // --- get node index
            int j;
            int err = _epanet.ENgetnodeindex(tok[1], out j);
            if (err != 0) return InpErrorCodes.ERR_NAME;

            //  --- get species index
            int m = _project.MSXproj_findObject(ObjectTypes.SPECIES, tok[2]);
            if (m <= 0) return InpErrorCodes.ERR_NAME;

            // --- check that species is a BULK species
            if (_msx.Species[m].Type != SpeciesType.BULK) return 0;

            // --- get base strength
            double x;
            if (!tok[3].ToDouble(out x)) return InpErrorCodes.ERR_NUMBER;
       
            // --- get time pattern if present
            var i = 0;
            if (tok.Length >= 5) {
                i = _project.MSXproj_findObject(ObjectTypes.PATTERN, tok[4]);
                if (i <= 0) return InpErrorCodes.ERR_NAME;
            }

            // --- check if a source for this species already exists
            foreach (Source src  in  _msx.Node[j].Sources) {
                if (src.Species == m) {
                    source = src;
                    break;
                }

            }

            // --- otherwise create a new source object
            if (source == null) {
                source = new Source(); //(struct Ssource *) malloc(sizeof(struct Ssource));
                //if ( source == NULL ) return 101;
                //source->next = MSX.Node[j].sources;
                //MSX.Node[j].sources = source;
                _msx.Node[j].Sources.Insert(0, source);
            }

            // --- save source's properties

            source.Type = (SourceType)k;
            source.Species = m;
            source.C0 = x;
            source.Pattern = i;
            return 0;
        }

        /// <summary>Parses an input line containing a time pattern data.</summary>
        private InpErrorCodes ParsePattern(string[] tok) {

            // --- get time pattern index
            if (tok.Length < 2) return InpErrorCodes.ERR_ITEMS;
            int i = _project.MSXproj_findObject(ObjectTypes.PATTERN, tok[0]);
            if (i <= 0) return InpErrorCodes.ERR_NAME;
            _msx.Pattern[i].Id = _project.MSXproj_findID(ObjectTypes.PATTERN, tok[0]);

            // --- begin reading pattern multipliers


            for (int k = 1; k < tok.Length; k++) //string token : Tok)
            {
                double x;
                if (!tok[k].ToDouble(out x)) return InpErrorCodes.ERR_NUMBER;

                _msx.Pattern[i].Multipliers.Add(x);


                // k++;
            }
            return 0;
        }

        private InpErrorCodes ParseReport(string[] tok) {
            int err;

            // Get keyword
            if (tok.Length < 2)
                return 0;

            int k = Utilities.MSXutils_findmatch(tok[0], Constants.ReportWords);

            if (k < 0)
                return InpErrorCodes.ERR_KEYWORD;

            switch (k) {
            // Keyword is NODE; parse ID names of reported nodes
            case 0:
                if (string.Equals(tok[1], Constants.ALL, StringComparison.OrdinalIgnoreCase)) {
                    for (int j = 1; j <= _msx.Nobjects[(int)ObjectTypes.NODE]; j++)
                        _msx.Node[j].Rpt = true;
                }
                else if (string.Equals(tok[1], Constants.NONE, StringComparison.OrdinalIgnoreCase)) {
                    for (int j = 1; j <= _msx.Nobjects[(int)ObjectTypes.NODE]; j++)
                        _msx.Node[j].Rpt = false;
                }
                else
                    for (int i = 1; i < tok.Length; i++) {
                        int j;
                        err = _epanet.ENgetnodeindex(tok[i], out j);

                        if (err != 0)
                            return InpErrorCodes.ERR_NAME;

                        _msx.Node[j].Rpt = true;
                    }
                break;

            // Keyword is LINK: parse ID names of reported links
            case 1:
                if (string.Equals(tok[1], Constants.ALL, StringComparison.OrdinalIgnoreCase)) {
                    for (int j = 1; j <= _msx.Nobjects[(int)ObjectTypes.LINK]; j++)
                        _msx.Link[j].Rpt = true;
                }
                else if (string.Equals(tok[1], Constants.NONE, StringComparison.OrdinalIgnoreCase)) {
                    for (int j = 1; j <= _msx.Nobjects[(int)ObjectTypes.LINK]; j++)
                        _msx.Link[j].Rpt = false;
                }
                else
                    for (int i = 1; i < tok.Length; i++) {
                        int j;
                        err = _epanet.ENgetlinkindex(tok[i], out j);
                        if (err != 0) return InpErrorCodes.ERR_NAME;
                        _msx.Link[j].Rpt = true;
                    }
                break;

            // Keyword is SPECIES; get YES/NO & precision
            case 2: {
                int j = _project.MSXproj_findObject(ObjectTypes.SPECIES, tok[1]);
                if (j <= 0) return InpErrorCodes.ERR_NAME;

                if (tok.Length >= 3) {
                    if (string.Equals(tok[2], Constants.YES, StringComparison.OrdinalIgnoreCase)) _msx.Species[j].Rpt = 1;
                    else if (string.Equals(tok[2], Constants.NO, StringComparison.OrdinalIgnoreCase)) _msx.Species[j].Rpt = 0;
                    else return InpErrorCodes.ERR_KEYWORD;
                }

                if (tok.Length >= 4) {
                    int i;
                    // BUG: Baseform bug
                    if (!int.TryParse(tok[3], out i)) ;
                    _msx.Species[j].Precision = i;
                    return InpErrorCodes.ERR_NUMBER;
                }
            }
                break;

            // Keyword is FILE: get name of report file
            case 3:
                _msx.RptFilename = tok[1];
                break;

            // Keyword is PAGESIZE;
            case 4: {
                int i;
                if (!int.TryParse(tok[1], out i))
                    return InpErrorCodes.ERR_NUMBER;
                _msx.PageSize = i;
            }
                break;
            }
            return 0;
        }

        /// <summary>
        ///  Finds the index assigned to a species, intermediate term, parameter, or constant that appears in a math expression.
        /// </summary>
        private int GetVariableCode(string id) {
            int j = _project.MSXproj_findObject(ObjectTypes.SPECIES, id);

            if (j >= 1) return j;

            j = _project.MSXproj_findObject(ObjectTypes.TERM, id);

            if (j >= 1) return _msx.Nobjects[(int)ObjectTypes.SPECIES] + j;

            j = _project.MSXproj_findObject(ObjectTypes.PARAMETER, id);

            if (j >= 1)
                return _msx.Nobjects[(int)ObjectTypes.SPECIES]
                       + _msx.Nobjects[(int)ObjectTypes.TERM] +
                       j;

            j = _project.MSXproj_findObject(ObjectTypes.CONSTANT, id);

            if (j >= 1)
                return _msx.Nobjects[(int)ObjectTypes.SPECIES]
                       + _msx.Nobjects[(int)ObjectTypes.TERM]
                       + _msx.Nobjects[(int)ObjectTypes.PARAMETER] + j;

            j = Utilities.MSXutils_findmatch(id, Constants.HydVarWords);

            if (j >= 1)
                return _msx.Nobjects[(int)ObjectTypes.SPECIES]
                       + _msx.Nobjects[(int)ObjectTypes.TERM]
                       + _msx.Nobjects[(int)ObjectTypes.PARAMETER]
                       + _msx.Nobjects[(int)ObjectTypes.CONSTANT] + j;
            return -1;
        }

        private static void WriteInpErrMsg(InpErrorCodes errcode, string sect, string line, int lineCount) {

            if (errcode >= InpErrorCodes.INP_ERR_LAST || errcode <= InpErrorCodes.INP_ERR_FIRST) {
                Console.Error.WriteLine("Error Code = {0}", (int)errcode);
            }
            else {
                Console.Error.WriteLine(
                           "{0} at line {1} of {2}] section:",
                           inpErrorTxt[errcode - InpErrorCodes.INP_ERR_FIRST],
                           lineCount,
                           sect);
            }

        }

    }

}
