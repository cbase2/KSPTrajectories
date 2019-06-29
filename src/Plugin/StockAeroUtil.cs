﻿/*
  Copyright© (c) 2015-2017 Youen Toupin, (aka neuoy).
  Copyright© (c) 2017-2018 A.Korsunsky, (aka fat-lobyte).

  This file is part of Trajectories.
  Trajectories is available under the terms of GPL-3.0-or-later.
  See the LICENSE.md file for more details.

  Trajectories is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  Trajectories is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.

  You should have received a copy of the GNU General Public License
  along with Trajectories.  If not, see <http://www.gnu.org/licenses/>.
*/

// StockAeroUtil by atomicfury.
 
//#define PRECOMPUTE_CACHE
using System;
using System.Reflection;
using UnityEngine;

namespace Trajectories
{
    
    // this class provides several methods to access stock aero information
    public static class StockAeroUtil
    {
        /// <summary>
        /// This function should return exactly the same value as Vessel.atmDensity, but is more generic because you don't need an actual vessel updated by KSP to get a value at the desired location.
        /// Computations are performed for the current body position, which means it's theoritically wrong if you want to know the temperature in the future, but since body rotation is not used (position is given in sun frame), you should get accurate results up to a few weeks.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public static double GetTemperature(Vector3d position, CelestialBody body)
        {
            if (!body.atmosphere)
                return PhysicsGlobals.SpaceTemperature;

            double altitude = (position - body.position).magnitude - body.Radius;
            if (altitude > body.atmosphereDepth)
                return PhysicsGlobals.SpaceTemperature;

            Vector3 up = (position - body.position).normalized;
            float polarAngle = Mathf.Acos(Vector3.Dot(body.bodyTransform.up, up));
            if (polarAngle > Mathf.PI / 2.0f)
            {
                polarAngle = Mathf.PI - polarAngle;
            }
            float time = (Mathf.PI / 2.0f - polarAngle) * 57.29578f;

            Vector3 sunVector = (FlightGlobals.Bodies[0].position - position).normalized;
            float sunAxialDot = Vector3.Dot(sunVector, body.bodyTransform.up);
            float bodyPolarAngle = Mathf.Acos(Vector3.Dot(body.bodyTransform.up, up));
            float sunPolarAngle = Mathf.Acos(sunAxialDot);
            float sunBodyMaxDot = (1.0f + Mathf.Cos(sunPolarAngle - bodyPolarAngle)) * 0.5f;
            float sunBodyMinDot = (1.0f + Mathf.Cos(sunPolarAngle + bodyPolarAngle)) * 0.5f;
            float sunDotCorrected = (1.0f + Vector3.Dot(sunVector, Quaternion.AngleAxis(45f * Mathf.Sign((float)body.rotationPeriod), body.bodyTransform.up) * up)) * 0.5f;
            float sunDotNormalized = (sunDotCorrected - sunBodyMinDot) / (sunBodyMaxDot - sunBodyMinDot);
            double atmosphereTemperatureOffset = (double)body.latitudeTemperatureBiasCurve.Evaluate(time) + (double)body.latitudeTemperatureSunMultCurve.Evaluate(time) * sunDotNormalized + (double)body.axialTemperatureSunMultCurve.Evaluate(sunAxialDot);
            double temperature = body.GetTemperature(altitude) + (double)body.atmosphereTemperatureSunMultCurve.Evaluate((float)altitude) * atmosphereTemperatureOffset;

            return temperature;
        }

        /// <summary>
        /// Gets the air density (rho) for the specified altitude on the specified body.
        /// This is an approximation, because actual calculations, taking sun exposure into account to compute air temperature, require to know the actual point on the body where the density is to be computed (knowing the altitude is not enough).
        /// However, the difference is small for high altitudes, so it makes very little difference for trajectory prediction.
        /// </summary>
        /// <param name="altitude">Altitude above sea level (in meters)</param>
        /// <param name="body"></param>
        /// <returns></returns>
        public static double GetDensity(double altitude, CelestialBody body)
        {
            if (!body.atmosphere)
                return 0;

            if (altitude > body.atmosphereDepth)
                return 0;

            double pressure = body.GetPressure(altitude);

            // get an average day/night temperature at the equator
            double sunDot = 0.5;
            float sunAxialDot = 0;
            double atmosphereTemperatureOffset = (double)body.latitudeTemperatureBiasCurve.Evaluate(0)
                + (double)body.latitudeTemperatureSunMultCurve.Evaluate(0) * sunDot
                + (double)body.axialTemperatureSunMultCurve.Evaluate(sunAxialDot);
            double temperature = // body.GetFullTemperature(altitude, atmosphereTemperatureOffset);
                body.GetTemperature(altitude)
                + (double)body.atmosphereTemperatureSunMultCurve.Evaluate((float)altitude) * atmosphereTemperatureOffset;


            return body.GetDensity(pressure, temperature);
        }

        public static double GetDensity(Vector3d position, CelestialBody body)
        {
            if (!body.atmosphere)
                return 0;

            double altitude = (position - body.position).magnitude - body.Radius;
            if (altitude > body.atmosphereDepth)
                return 0;

            double pressure = body.GetPressure(altitude);
            double temperature = // body.GetFullTemperature(position);
                GetTemperature(position, body);

            return body.GetDensity(pressure, temperature);
        }

        //*******************************************************
        public static Vector3 SimAeroForce(Vessel _vessel, Vector3 v_wrld_vel, Vector3 position)
        {
            CelestialBody body = _vessel.mainBody;
            double latitude = body.GetLatitude(position) / 180.0 * Math.PI;
            double altitude = (position - body.position).magnitude - body.Radius;

            return SimAeroForce(_vessel, v_wrld_vel, altitude, latitude);
        }

        //*******************************************************
        public static Vector3 SimAeroForce(Vessel _vessel, Vector3 v_wrld_vel, double altitude, double latitude = 0.0)
        {
            Profiler.Start("SimAeroForce");
            Quaternion toLocal = _vessel.transform.localRotation.Inverse();
            //if (VesselAerodynamicModel.DebugParts)
               // Debug.Log("alt: " + altitude + " vel: " + (toLocal * v_wrld_vel.normalized).ToString("F1") );

            FieldInfo mcsCtrlSurfacefield = typeof(ModuleControlSurface).GetField("ctrlSurface", BindingFlags.Instance | BindingFlags.NonPublic);

            CelestialBody body = _vessel.mainBody;
            double pressure = body.GetPressure(altitude);
            // Lift and drag for force accumulation.
            Vector3d total_lift = Vector3d.zero;
            Vector3d total_drag = Vector3d.zero;

            // dynamic pressure for standard drag equation
            double rho = GetDensity(altitude, body);
            double dyn_pressure = 0.0005 * rho * v_wrld_vel.sqrMagnitude;

            if (rho <= 0)
            {
                return Vector3.zero;
            }

            double soundSpeed = body.GetSpeedOfSound(pressure, rho);
            double mach = v_wrld_vel.magnitude / soundSpeed;
            if (mach > 25.0) { mach = 25.0; }
            float pseudoreynolds = (float)(rho * v_wrld_vel.magnitude);
            float pseudoredragmult = PhysicsGlobals.DragCurvePseudoReynolds.Evaluate(pseudoreynolds);

            // Loop through all parts, accumulating drag and lift.
            for (int i = 0; i < _vessel.Parts.Count; ++i)
            {
                // need checks on shielded components
                Part p = _vessel.Parts[i];
#if DEBUG

                TrajectoriesDebug partDebug = VesselAerodynamicModel.DebugParts ? p.FindModuleImplementing<TrajectoriesDebug>() : null;
                if (partDebug != null)
                {
                    partDebug.Drag = Vector3.zero;
                    partDebug.Lift = Vector3.zero;
                }
#endif

                if (p.ShieldedFromAirstream || p.Rigidbody == null)
                {
                    continue;
                }

                // Get Drag
                Vector3 sim_dragVectorDir = v_wrld_vel.normalized;
                Vector3 sim_dragVectorDirLocal = -(p.transform.InverseTransformDirection(sim_dragVectorDir));

                Vector3 liftForce = new Vector3(0, 0, 0);
                Vector3d dragForce;


                Profiler.Start("SimAeroForce#drag");
                switch (p.dragModel)
                {
                    case Part.DragModel.DEFAULT:
                    case Part.DragModel.CUBE:
                        DragCubeList cubes = p.DragCubes;

                        DragCubeList.CubeData p_drag_data = new DragCubeList.CubeData();

                        float drag;
                        if (cubes.None) // since 1.0.5, some parts don't have drag cubes (for example fuel lines and struts)
                        {
                            drag = p.maximum_drag;
                        }
                        else
                        {
                            try
                            {
                                cubes.AddSurfaceDragDirection(-sim_dragVectorDirLocal, (float)mach, ref p_drag_data);
                            }
                            catch (Exception)
                            {
                                cubes.SetDrag(sim_dragVectorDirLocal, (float)mach);
                                cubes.ForceUpdate(true, true);
                                cubes.AddSurfaceDragDirection(-sim_dragVectorDirLocal, (float)mach, ref p_drag_data);
                                //Debug.Log(String.Format("Trajectories: Caught NRE on Drag Initialization.  Should be fixed now.  {0}", e));
                            }

                            drag = p_drag_data.areaDrag * PhysicsGlobals.DragCubeMultiplier * pseudoredragmult;

                            liftForce = p_drag_data.liftForce;
                        }

                        double sim_dragScalar = dyn_pressure * (double)drag * PhysicsGlobals.DragMultiplier;
                        dragForce = -(Vector3d)sim_dragVectorDir * sim_dragScalar;

                        break;

                    case Part.DragModel.SPHERICAL:
                        dragForce = -(Vector3d)sim_dragVectorDir * (double)p.maximum_drag;
                        break;

                    case Part.DragModel.CYLINDRICAL:
                        dragForce = -(Vector3d)sim_dragVectorDir * (double)Mathf.Lerp(p.minimum_drag, p.maximum_drag, Mathf.Abs(Vector3.Dot(p.partTransform.TransformDirection(p.dragReferenceVector), sim_dragVectorDir)));
                        break;

                    case Part.DragModel.CONIC:
                        dragForce = -(Vector3d)sim_dragVectorDir * (double)Mathf.Lerp(p.minimum_drag, p.maximum_drag, Vector3.Angle(p.partTransform.TransformDirection(p.dragReferenceVector), sim_dragVectorDir) / 180f);
                        break;

                    default:
                        // no drag to apply
                        dragForce = new Vector3d();
                        break;
                }

                Profiler.Stop("SimAeroForce#drag");

#if DEBUG
                if (partDebug != null)
                {
                    partDebug.Drag += dragForce;
                }
                #endif
                total_drag += dragForce;

                // If it isn't a wing or lifter, get body lift.
                if (!p.hasLiftModule)
                {
                    Profiler.Start("SimAeroForce#BodyLift");

                    float simbodyLiftScalar = p.bodyLiftMultiplier * PhysicsGlobals.BodyLiftMultiplier * (float)dyn_pressure;
                    simbodyLiftScalar *= PhysicsGlobals.GetLiftingSurfaceCurve("BodyLift").liftMachCurve.Evaluate((float)mach);
                    Vector3 bodyLift = p.transform.rotation * (simbodyLiftScalar * liftForce);
                    bodyLift = Vector3.ProjectOnPlane(bodyLift, sim_dragVectorDir);
                    // Only accumulate forces for non-LiftModules
                    total_lift += bodyLift;
#if DEBUG
                    if (partDebug != null)
                    {
                        partDebug.Lift += bodyLift;
                    }
#endif

                    Profiler.Stop("SimAeroForce#BodyLift");
                }


                Profiler.Start("SimAeroForce#LiftingSurface");

                // Find ModuleLifingSurface for wings and liftforce.
                // Should catch control surface as it is a subclass
                for (int j = 0; j < p.Modules.Count; ++j)
                {
                    var m = p.Modules[j];
                    float mcs_mod = 0f;
                    //Quaternion mcs_rot = Quaternion.identity;
                    Vector3 mcs_up = Vector3.zero;
                    if (m is ModuleLiftingSurface)
                    {

                        float liftQ = (float) dyn_pressure * 1000;
                        ModuleLiftingSurface wing = (ModuleLiftingSurface)m;
                        if (wing is ModuleControlSurface)
                        {
                            mcs_mod = (wing as ModuleControlSurface).ctrlSurfaceArea;
                            // use reflections to access protected field ctrlSurface
                            mcs_up = (mcsCtrlSurfacefield.GetValue(wing as ModuleControlSurface) as Transform).forward;
                            
                        }

                        Vector3 liftVector = wing.transformSign * p.transform.forward;
                        float liftdot = Vector3.Dot(sim_dragVectorDir, liftVector);
                        float absdot = Mathf.Abs(liftdot);
                        //wing.SetupCoefficients(v_wrld_vel, out nVel, out liftVector, out liftdot, out absdot);

                        //double prevMach = p.machNumber;
                        //p.machNumber = mach;

                        // fixed wing proportion for control surfaces
                        Vector3 local_lift = (1.0f - mcs_mod) * Mathf.Sign(liftdot)
                                                              * wing.liftCurve.Evaluate(absdot)
                                                              * wing.liftMachCurve.Evaluate((float)mach)
                                                              * wing.deflectionLiftCoeff
                                                              * liftQ * PhysicsGlobals.LiftMultiplier
                                                              * -liftVector;

                        //(wing.GetLiftVector(liftVector, liftdot, absdot, liftQ, (float)mach));
                        Vector3 local_drag = (1.0f - mcs_mod) * wing.dragCurve.Evaluate(absdot)
                                                              * wing.dragMachCurve.Evaluate((float)mach)
                                                              * wing.deflectionLiftCoeff
                                                              * liftQ * PhysicsGlobals.LiftDragMultiplier
                                                              * -sim_dragVectorDir;


                            //(wing.GetDragVector(nVel, absdot, liftQ));

                        total_lift += local_lift;
                        total_drag += local_drag;
#if DEBUG
                        if (partDebug != null)
                        {
                            partDebug.Lift += local_lift;
                            partDebug.Drag += local_drag;
                        }
#endif

                        if (wing is ModuleControlSurface)
                        {
                            liftVector = wing.transformSign * mcs_up;
                            liftdot = Vector3.Dot(sim_dragVectorDir, liftVector);
                            absdot = Mathf.Abs(liftdot);
                            
                            local_lift = mcs_mod * Mathf.Sign(liftdot)
                                                 * wing.liftCurve.Evaluate(absdot)
                                                 * wing.liftMachCurve.Evaluate((float)mach)
                                                 * wing.deflectionLiftCoeff
                                                 * liftQ * PhysicsGlobals.LiftMultiplier
                                                 * -liftVector;

                            local_drag = mcs_mod * wing.dragCurve.Evaluate(absdot)
                                                 * wing.dragMachCurve.Evaluate((float)mach)
                                                 * wing.deflectionLiftCoeff
                                                 * liftQ * PhysicsGlobals.LiftDragMultiplier
                                                 * -sim_dragVectorDir;

                            total_lift += local_lift;
                            total_drag += local_drag;
#if DEBUG
                            if (partDebug != null)
                            {
                                partDebug.Lift += local_lift;
                                partDebug.Drag += local_drag;
                               // Debug.Log(String.Format("Traj Part MCS: localLift={0:F1}, local nVel={1:F1}, liftVector={2:F1}, liftdot={3:F1}, absDot={4:F1}", toLocal * local_lift, toLocal * sim_dragVectorDir, toLocal * liftVector, liftdot, absdot));
                            }
#endif
                        }
                        //p.machNumber = prevMach;

                    }
                }

                Profiler.Stop("SimAeroForce#LiftingSurface");

            }
            // RETURN STUFF
            Vector3 force = total_lift + total_drag;

            Profiler.Stop("SimAeroForce");
            return force;
        }
    } //StockAeroUtil
}

