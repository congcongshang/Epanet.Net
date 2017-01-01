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
using System.Collections.Generic;

using Epanet.Enums;

namespace Epanet.Network {

    /// <summary>Simulation configuration configuration.</summary>
    public sealed class PropertiesMap {

        private Dictionary<string, string> extraOptions;

        public PropertiesMap() { this.LoadDefaults(); }

        #region properties accessors/mutators

        public string AltReport { get; set; }

        /// <summary>Bulk flow reaction order</summary>
        public double BulkOrder { get; set; }

        /// <summary>Hydraulics solver parameter.</summary>
        public int CheckFreq { get; set; }

        /// <summary>Name of chemical.</summary>
        public string ChemName { get; set; }

        /// <summary>Units of chemical.</summary>
        public string ChemUnits { get; set; }

        /// <summary>Limiting potential quality.</summary>
        public double CLimit { get; set; }

        /// <summary>Water quality tolerance.</summary>
        public double Ctol { get; set; }

        /// <summary>Solution damping threshold.</summary>
        public double DampLimit { get; set; }

        /// <summary>Energy demand charge/kw/day.</summary>
        public double DCost { get; set; }

        /// <summary>Default demand pattern ID.</summary>
        public string DefPatId { get; set; }

        /// <summary>Diffusivity (sq ft/sec).</summary>
        public double Diffus { get; set; }

        /// <summary>Demand multiplier.</summary>
        public double DMult { get; set; }

        /// <summary>Duration of simulation (sec).</summary>
        public long Duration { get; set; }

        /// <summary>Base energy cost per kwh.</summary>
        public double ECost { get; set; }

        /// <summary>Peak energy usage.</summary>
        public double EMax { get; set; }

        /// <summary>Energy report flag.</summary>
        public bool EnergyFlag { get; set; }

        /// <summary>Energy cost time pattern.</summary>
        public string EPatId { get; set; }

        /// <summary>Global pump efficiency.</summary>
        public double EPump { get; set; }

        /// <summary>Extra hydraulic trials.</summary>
        public int ExtraIter { get; set; }

        /// <summary>Flow units flag.</summary>
        public FlowUnitsType FlowFlag { get; set; }

        /// <summary>Hydraulic formula flag.</summary>
        public FormType FormFlag { get; set; }

        /// <summary>Hydraulics solution accuracy.</summary>
        public double HAcc { get; set; }

        /// <summary>Exponent in headloss formula.</summary>
        public double HExp { get; set; }

        /// <summary>Nominal hyd. time step (sec).</summary>
        public long HStep { get; set; }

        /// <summary>Hydraulic head tolerance.</summary>
        public double HTol { get; set; }

        /// <summary>Hydraulics flag.</summary>
        public HydType HydFlag { get; set; }

        /// <summary>Hydraulics file name.</summary>
        public string HydFname { get; set; }

        /// <summary>Global bulk reaction coeff.</summary>
        public double KBulk { get; set; }

        /// <summary>Global wall reaction coeff.</summary>
        public double KWall { get; set; }

        /// <summary>Link report flag.</summary>
        public ReportFlag LinkFlag { get; set; }

        /// <summary>Map file name.</summary>
        public string MapFname { get; set; }

        /// <summary>Hydraulics solver parameter.</summary>
        public int MaxCheck { get; set; }

        /// <summary>Max. hydraulic trials.</summary>
        public int MaxIter { get; set; }

        /// <summary>Error/warning message flag.</summary>
        public bool MessageFlag { get; set; }

        /// <summary>Node report flag.</summary>
        public ReportFlag NodeFlag { get; set; }

        /// <summary>Lines/page in output report.</summary>
        public int PageSize { get; set; }

        /// <summary>Pressure units flag.</summary>
        public PressUnitsType PressFlag { get; set; }

        /// <summary>Starting pattern time (sec).</summary>
        public long PStart { get; set; }

        /// <summary>Time pattern time step (sec).</summary>
        public long PStep { get; set; }

        /// <summary>Exponent in orifice formula.</summary>
        public double QExp { get; set; }

        /// <summary>Quality time step (sec).</summary>
        public long QStep { get; set; }

        /// <summary>Flow rate tolerance.</summary>
        public double QTol { get; set; }

        /// <summary>Water quality flag.</summary>
        public QualType QualFlag { get; set; }

        /// <summary>Roughness-reaction factor.</summary>
        public double RFactor { get; set; }

        /// <summary>Flow resistance tolerance.</summary>
        public double RQtol { get; set; }

        /// <summary>Time when reporting starts.</summary>
        public long RStart { get; set; }

        /// <summary>Reporting time step (sec).</summary>
        public long RStep { get; set; }

        /// <summary>Rule evaluation time step.</summary>
        public long RuleStep { get; set; }

        /// <summary>Specific gravity.</summary>
        public double SpGrav { get; set; }

        /// <summary>Status report flag.</summary>
        public StatFlag Stat_Flag { get; set; }

        /// <summary>Report summary flag.</summary>
        public bool SummaryFlag { get; set; }

        /// <summary>Tank reaction order.</summary>
        public double TankOrder { get; set; }

        /// <summary>Source node for flow tracing.</summary>
        public string TraceNode { get; set; }

        /// <summary>Starting time of day (sec).</summary>
        public long TStart { get; set; }

        /// <summary>Time statistics flag.</summary>
        public TStatType TStatFlag { get; set; }

        /// <summary>Unit system flag.</summary>
        public UnitsType UnitsFlag { get; set; }

        /// <summary>Kin. viscosity (sq ft/sec).</summary>
        public double Viscos { get; set; }

        /// <summary>Pipe wall reaction order.</summary>
        public double WallOrder { get; set; }

        #endregion

        public Dictionary<string, string> ExtraOptions {
            get {
                return this.extraOptions ?? (this.extraOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }
        }


        /// <summary>Init properties with default value.</summary>
        private void LoadDefaults() {
            this.BulkOrder = 1.0d; // 1st-order bulk reaction rate
            this.TankOrder = 1.0d; // 1st-order tank reaction rate
            this.WallOrder = 1.0d; // 1st-order wall reaction rate
            this.RFactor = 1.0d; // No roughness-reaction factor
            this.CLimit = 0.0d; // No limiting potential quality
            this.KBulk = 0.0d; // No global bulk reaction
            this.KWall = 0.0d; // No global wall reaction
            this.DCost = 0.0d; // Zero energy demand charge
            this.ECost = 0.0d; // Zero unit energy cost
            this.EPatId = string.Empty; // No energy price pattern
            this.EPump = Constants.EPUMP; // Default pump efficiency
            this.PageSize = Constants.PAGESIZE;
            this.Stat_Flag = StatFlag.NO;
            this.SummaryFlag = true;
            this.MessageFlag = true;
            this.EnergyFlag = false;
            this.NodeFlag = ReportFlag.FALSE;
            this.LinkFlag = ReportFlag.FALSE;
            this.TStatFlag = TStatType.SERIES; // Generate time series output
            this.HStep = 3600L; // 1 hr hydraulic time step
            this.Duration = 0L; // 0 sec duration (steady state)
            this.QStep = 0L; // No pre-set quality time step
            this.RuleStep = 0L; // No pre-set rule time step
            this.PStep = 3600L; // 1 hr time pattern period
            this.PStart = 0L; // Starting pattern period
            this.RStep = 3600L; // 1 hr reporting period
            this.RStart = 0L; // Start reporting at time 0
            this.TStart = 0L; // Starting time of day
            this.FlowFlag = FlowUnitsType.GPM; // Flow units are gpm
            this.PressFlag = PressUnitsType.PSI; // Pressure units are psi
            this.FormFlag = FormType.HW; // Use Hazen-Williams formula
            this.HydFlag = HydType.SCRATCH; // No external hydraulics file
            this.QualFlag = QualType.NONE; // No quality simulation
            this.UnitsFlag = UnitsType.US; // US unit system
            this.HydFname = "";
            this.ChemName = Keywords.t_CHEMICAL;
            this.ChemUnits = Keywords.u_MGperL; // mg/L
            this.DefPatId = Constants.DEFPATID; // Default demand pattern index
            this.MapFname = "";
            this.AltReport = "";
            this.TraceNode = ""; // No source tracing
            this.ExtraIter = -1; // Stop if network unbalanced
            this.Ctol = double.NaN; // No pre-set quality tolerance
            this.Diffus = double.NaN; // Temporary diffusivity
            this.DampLimit = Constants.DAMPLIMIT;
            this.Viscos = double.NaN; // Temporary viscosity
            this.SpGrav = Constants.SPGRAV; // Default specific gravity
            this.MaxIter = Constants.MAXITER; // Default max. hydraulic trials
            this.HAcc = Constants.HACC; // Default hydraulic accuracy
            this.HTol = Constants.HTOL; // Default head tolerance
            this.QTol = Constants.QTOL; // Default flow tolerance
            this.RQtol = Constants.RQTOL; // Default hydraulics parameters
            this.HExp = 0.0d;
            this.QExp = 2.0d; // Flow exponent for emitters
            this.CheckFreq = Constants.CHECKFREQ;
            this.MaxCheck = Constants.MAXCHECK;
            this.DMult = 1.0d; // Demand multiplier
            this.EMax = 0.0d; // Zero peak energy usage
        }



    }

}