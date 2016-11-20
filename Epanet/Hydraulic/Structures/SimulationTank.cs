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
using Epanet.Network;
using Epanet.Network.Structures;

namespace Epanet.Hydraulic.Structures {

    public class SimulationTank:SimulationNode {
        public SimulationTank(Node @ref, int idx):base(@ref, idx) {
            this.volume = ((Tank)this.node).V0;

            // Init
            this.head = ((Tank)this.node).H0;
            this.demand = 0.0;
            this.OldStat = Link.StatType.TEMPCLOSED;
        }

        // public Tank Node { get { return (Tank)this.node; } }

        private double volume;

        public double Area { get { return ((Tank)this.node).Area; } }

        public double Hmin { get { return ((Tank)this.node).Hmin; } }

        public double Hmax { get { return ((Tank)this.node).Hmax; } }

        public double Vmin { get { return ((Tank)this.node).Vmin; } }

        public double Vmax { get { return ((Tank)this.node).Vmax; } }

        public double V0 { get { return ((Tank)this.node).V0; } }

        public Pattern Pattern { get { return ((Tank)this.node).Pattern; } }

        public Curve Vcurve { get { return ((Tank)this.node).Vcurve; } }

#if COMMENTED

        public double H0 { get { return ((Tank)this.node).H0; } }

        public double Kb { get { return ((Tank)this.node).Kb; } }

        public double[] Concentration { get { return ((Tank)this.node).Concentration; } }

        public Tank.MixType MixModel { get { return ((Tank)this.node).MixModel; } }

        public double V1Max { get { return ((Tank)this.node).V1Max; } }

#endif

        /// Simulation getters & setters.
        public double SimVolume { get { return this.volume; } }

        public bool IsReservoir { get { return ((Tank)this.node).Area == 0; } }

        public Link.StatType OldStat { get; set; }

        /// Simulation methods

        ///<summary>Finds water volume in tank corresponding to elevation 'h'</summary>
        public double FindVolume(FieldsMap fMap, double h) {

            Curve curve = this.Vcurve;
            if (curve == null)
                return this.Vmin + (h - this.Hmin) * this.Area;
            else {
                return
                    curve[(h - this.Elevation) * fMap.GetUnits(FieldsMap.FieldType.HEAD)
                          / fMap.GetUnits(FieldsMap.FieldType.VOLUME)];
            }

        }

        /// <summary>Computes new water levels in tank after current time step, with Euler integrator.</summary>
        private void UpdateLevel(FieldsMap fMap, long tstep) {

            if (this.Area == 0.0) // Reservoir
                return;

            // Euler
            double dv = this.demand * tstep;
            this.volume += dv;

            if (this.volume + this.demand >= this.Vmax)
                this.volume = this.Vmax;

            if (this.volume - this.demand <= this.Vmin)
                this.volume = this.Vmin;

            this.head = this.FindGrade(fMap);
        }

        /// <summary>Finds water level in tank corresponding to current volume.</summary>
        private double FindGrade(FieldsMap fMap) {
            Curve curve = this.Vcurve;
            if (curve == null)
                return this.Hmin + (this.volume - this.Vmin) / this.Area;
            else
                return this.Elevation
                       + curve[this.volume * fMap.GetUnits(FieldsMap.FieldType.VOLUME)]
                       / fMap.GetUnits(FieldsMap.FieldType.HEAD);
        }

        /// <summary>Get the required time step based to fill or drain a tank.</summary>
        private long GetRequiredTimeStep(long tstep) {
            if (this.IsReservoir) return tstep; //  Skip reservoirs

            double h = this.head; // Current tank grade
            double q = this.demand; // Flow into tank
            double v = 0.0;

            if (Math.Abs(q) <= Constants.QZERO)
                return tstep;

            if (q > 0.0 && h < this.Hmax)
                v = this.Vmax - this.SimVolume; // Volume to fill
            else if (q < 0.0 && h > this.Hmin)
                v = this.Vmin - this.SimVolume; // Volume to drain
            else
                return tstep;

            // Compute time to fill/drain
            long t = (long)Math.Round(v / q);

            // Revise time step
            if (t > 0 && t < tstep)
                tstep = t;

            return tstep;
        }

        /// <summary>Revises time step based on shortest time to fill or drain a tank.</summary>
        public static long MinimumTimeStep(List<SimulationTank> tanks, long tstep) {
            long newTStep = tstep;
            foreach (SimulationTank tank  in  tanks)
                newTStep = tank.GetRequiredTimeStep(newTStep);
            return newTStep;
        }

        /// <summary>Computes new water levels in tanks after current time step.</summary>
        public static void StepWaterLevels(List<SimulationTank> tanks, FieldsMap fMap, long tstep) {
            foreach (SimulationTank tank  in  tanks)
                tank.UpdateLevel(fMap, tstep);
        }

    }

}