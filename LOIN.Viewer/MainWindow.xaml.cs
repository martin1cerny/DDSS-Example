﻿using LOIN.Context;
using LOIN.Requirements;
using LOIN.Viewer.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.MvdXml;
using Xbim.MvdXml.DataManagement;

namespace LOIN.Viewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Model _model;

        public MainWindow()
        {
            InitializeComponent();
        }



        public ContextSelector ContextSelector
        {
            get { return (ContextSelector)GetValue(ContextSelectorProperty); }
            set { SetValue(ContextSelectorProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ContextSelector.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ContextSelectorProperty =
            DependencyProperty.Register("ContextSelector", typeof(ContextSelector), typeof(MainWindow), new PropertyMetadata(null));




        public List<BreakedownItemView> BreakedownItems
        {
            get { return (List<BreakedownItemView>)GetValue(BreakedownItemsProperty); }
            set { SetValue(BreakedownItemsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for BreakedownItems.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty BreakedownItemsProperty =
            DependencyProperty.Register("BreakedownItems", typeof(List<BreakedownItemView>), typeof(MainWindow), new PropertyMetadata(null));




        public List<ActorView> Actors
        {
            get { return (List<ActorView>)GetValue(ActorsProperty); }
            set { SetValue(ActorsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Actors.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ActorsProperty =
            DependencyProperty.Register("Actors", typeof(List<ActorView>), typeof(MainWindow), new PropertyMetadata(null));



        public List<MilestoneView> Milestones
        {
            get { return (List<MilestoneView>)GetValue(MilestonesProperty); }
            set { SetValue(MilestonesProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Milestones.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MilestonesProperty =
            DependencyProperty.Register("Milestones", typeof(List<MilestoneView>), typeof(MainWindow), new PropertyMetadata(null));




        public List<ReasonView> Reasons
        {
            get { return (List<ReasonView>)GetValue(ReasonsProperty); }
            set { SetValue(ReasonsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Reasons.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ReasonsProperty =
            DependencyProperty.Register("Reasons", typeof(List<ReasonView>), typeof(MainWindow), new PropertyMetadata(null));


        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Filter = "IFC File|*.ifc|IFC XML File|*.ifcxml",
                FilterIndex = 0,
                Title = "Select LOIN IFC File",
                FileName = App.Settings.LastIFC,
                InitialDirectory = System.IO.Path.GetDirectoryName(App.Settings.LastIFC)
            };
            if (dlg.ShowDialog() != true)
                return;

            var fileName = dlg.FileName;
            var logPath = Path.ChangeExtension(fileName, ".log");

            IfcStore.ModelProviderFactory.UseMemoryModelProvider();
            using (var model = IfcStore.Open(fileName))
            {
                using (var log = new StreamWriter(logPath))
                {
                    var mvd = GetMvd(true);
                    var engine = new MvdEngine(mvd, model);

                    var noMatch = true;
                    foreach (var root in engine.ConceptRoots)
                    {
                        var entities = model.Instances
                            .Where<IfcRoot>(r => root.AppliesTo(r))
                            .ToList();
                        if (entities.Any())
                            noMatch = false;

                        log.WriteLine($"{entities} entities found to be applicable to concept root {root.name}");
                        foreach (var concept in root.Concepts)
                        {
                            var definition = concept.Definitions?.FirstOrDefault()?.Body?.Value;
                            log.WriteLine($"  Testing concept {concept.name} [{concept.uuid}]");
                            if (!string.IsNullOrWhiteSpace(definition))
                                log.WriteLine($"  {definition}");

                            var requirements = (concept.Requirements ?? Array.Empty<RequirementsRequirement>())
                                .Select(e => e.GetExchangeRequirement())
                                .ToList();
                            if (requirements.Any())
                            {
                                log.WriteLine("    Exchange requirements:");
                                foreach (var er in requirements)
                                {
                                    log.WriteLine($"      {er.name} [{er.uuid}]");
                                }
                            }

                            foreach (var entity in entities)
                            {
                                var passes = concept.Test(entity, Concept.ConceptTestMode.ThroughRequirementRequirements);
                                log.WriteLine($"    [{passes.ToString().ToUpperInvariant()}]: #{entity.EntityLabel}{entity.GetType().Name.ToUpperInvariant()}=('{entity.Name};)");
                            }
                        }
                    }

                    if (noMatch)
                    {
                        log.WriteLine("No entities matched for validation");
                    }
                    log.Close();
                }
            }

            Process.Start(logPath);

        }

        private void SelectionToIFC_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "IFC|*.ifc|IFC XML|*.ifcXML",
                AddExtension = true,
                FilterIndex = 0,
                FileName = App.Settings.LastMVD,
                InitialDirectory = System.IO.Path.GetDirectoryName(App.Settings.LastMVD),
                Title = "Create MVD XML..."
            };
            if (dlg.ShowDialog() != true)
                return;

            // positive filter for context relations
            var requirementSets = ContextSelector.RequirementSets;
            var requirementsFiler = new HashSet<int>(requirementSets.Select(r => r.Entity.EntityLabel));

            // root elements for copy operation
            var breakedownRels = ContextSelector.Context.OfType<BreakedownItem>()
                .SelectMany(i => i.Relations)
                .Where(r => r.RelatedObjects.Any(o => requirementsFiler.Contains(o.EntityLabel)))
                .ToList();
            var milestoneRels = ContextSelector.Context.OfType<Milestone>()
                .SelectMany(i => i.Relations)
                .Where(r => r.RelatedObjects.Any(o => requirementsFiler.Contains(o.EntityLabel)))
                .ToList();
            var reasonRels = ContextSelector.Context.OfType<Reason>()
                .SelectMany(i => i.Relations)
                .Where(r => r.RelatedObjects.Any(o => requirementsFiler.Contains(o.EntityLabel)))
                .ToList();
            var actorRels = ContextSelector.Context.OfType<Actor>()
                .SelectMany(i => i.Relations)
                .Where(r => r.RelatedObjects.Any(o => requirementsFiler.Contains(o.EntityLabel)))
                .ToList();
            var declareRels = requirementSets.SelectMany(r => r.Relations);


            // positive filter for declared requirements
            var psets = ContextSelector.Requirements.Where(r => r.IsSelected).Select(r => r.PsetTemplate);
            if (!psets.Any())
                psets = ContextSelector.Requirements.Select(r => r.PsetTemplate);
            var definitionsFilter = new HashSet<IfcPropertySetTemplate>(psets);

            // actual copy logic
            var source = _model.Internal;
            using (var target = IfcStore.Create(Xbim.Common.Step21.XbimSchemaVersion.Ifc4, Xbim.IO.XbimStoreType.InMemoryModel))
            {
                using (var txn = target.BeginTransaction("Copy part of LOIN"))
                {
                    var map = new XbimInstanceHandleMap(source, target);

                    // use relations as roots, filter collections accordingly
                    foreach (var rel in breakedownRels)
                    {
                        target.InsertCopy(rel, map, (prop, obj) =>
                        {
                            if (prop.IsInverse)
                                return null;
                            if (obj is IfcRelAssociatesClassification rc && prop.Name == nameof(IfcRelAssociatesClassification.RelatedObjects))
                            {
                                return rc.RelatedObjects
                                    .Where(o => requirementsFiler.Contains(o.EntityLabel))
                                    .ToList();
                            }
                            return prop.PropertyInfo.GetValue(obj);
                        }, false, false);
                    }

                    foreach (var rel in milestoneRels)
                    {
                        target.InsertCopy(rel, map, (prop, obj) =>
                        {
                            if (prop.IsInverse)
                                return null;
                            if (obj is IfcRelAssignsToProcess rp && prop.Name == nameof(IfcRelAssignsToProcess.RelatedObjects))
                            {
                                return rp.RelatedObjects
                                    .Where(o => requirementsFiler.Contains(o.EntityLabel))
                                    .ToList();
                            }
                            return prop.PropertyInfo.GetValue(obj);
                        }, false, false);
                    }

                    foreach (var rel in reasonRels)
                    {
                        target.InsertCopy(rel, map, (prop, obj) =>
                        {
                            if (prop.IsInverse)
                                return null;
                            if (obj is IfcRelAssignsToControl rp && prop.Name == nameof(IfcRelAssignsToControl.RelatedObjects))
                            {
                                return rp.RelatedObjects
                                    .Where(o => requirementsFiler.Contains(o.EntityLabel))
                                    .ToList();
                            }
                            return prop.PropertyInfo.GetValue(obj);
                        }, false, false);
                    }

                    foreach (var rel in actorRels)
                    {
                        target.InsertCopy(rel, map, (prop, obj) =>
                        {
                            if (prop.IsInverse)
                                return null;
                            if (obj is IfcRelAssignsToActor rp && prop.Name == nameof(IfcRelAssignsToActor.RelatedObjects))
                            {
                                return rp.RelatedObjects
                                    .Where(o => requirementsFiler.Contains(o.EntityLabel))
                                    .ToList();
                            }
                            return prop.PropertyInfo.GetValue(obj);
                        }, false, false);
                    }

                    foreach (var rel in declareRels)
                    {
                        target.InsertCopy(rel, map, (prop, obj) =>
                        {
                            if (prop.IsInverse)
                                return null;
                            if (obj is IfcRelDeclares rp && prop.Name == nameof(IfcRelDeclares.RelatedDefinitions))
                            {
                                return rp.RelatedDefinitions
                                    .Where(o => definitionsFilter.Contains(o))
                                    .ToList();
                            }
                            return prop.PropertyInfo.GetValue(obj);
                        }, false, false);
                    }

                    txn.Commit();
                }
                target.SaveAs(dlg.FileName);
            }
        }

        private void SelectionToMVD_Click(object sender, RoutedEventArgs e)
        {
            ExportToMVD(true);
        }

        private void ExportToMVD_Click(object sender, RoutedEventArgs e)
        {
            ExportToMVD(false);
        }

        private mvdXML GetMvd(bool filtered)
        {
            if (!filtered)
                return _model.GetMvd("cs", "LOIN", "LOIN requirements stored as MVD", "LOIN", "DataTemplate ID", null, null);

            var breakedown = new HashSet<IContextEntity>(ContextSelector.Context.OfType<BreakedownItem>());
            var milestones = new HashSet<IContextEntity>(ContextSelector.Context.OfType<Milestone>());
            var reasons = new HashSet<IContextEntity>(ContextSelector.Context.OfType<Reason>());
            var actors = new HashSet<IContextEntity>(ContextSelector.Context.OfType<Actor>());

            var psets = ContextSelector.Requirements.Where(r => r.IsSelected).Select(r => r.PsetTemplate);
            if (!psets.Any())
                psets = ContextSelector.Requirements.Select(r => r.PsetTemplate);

            var requirements = new HashSet<IfcPropertySetTemplate>(psets);

            // filtered export
            return _model.GetMvd("cs", "LOIN", "LOIN requirements stored as MVD", "LOIN", "DataTemplate ID",
                c =>
                {
                    if (breakedown.Count > 0 && c is BreakedownItem i)
                        return breakedown.Contains(c);
                    if (milestones.Count > 0 && c is Milestone m)
                        return milestones.Contains(m);
                    if (reasons.Count > 0 && c is Reason r)
                        return reasons.Contains(r);
                    if (actors.Count > 0 && c is Actor a)
                        return actors.Contains(a);
                    return true;

                },
                p => requirements.Contains(p));
        }

        private void ExportToMVD(bool filtered)
        {
            if (_model == null)
            {
                MessageBox.Show(this, "No model opened", "No model", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mvd = GetMvd(filtered);

            var dlg = new SaveFileDialog
            {
                Filter = "MVD XML|*.mvdXML",
                AddExtension = true,
                FilterIndex = 0,
                FileName = App.Settings.LastMVD,
                InitialDirectory = System.IO.Path.GetDirectoryName(App.Settings.LastMVD),
                Title = "Create MVD XML..."
            };
            if (dlg.ShowDialog() != true)
                return;


            var path = dlg.FileName;
            App.Settings.LastMVD = path;
            if (string.IsNullOrWhiteSpace(path))
                return;

            using var stream = File.Create(path);
            var w = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, IndentChars = "  " });
            mvd.Serialize(w);
            stream.Close();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Filter = "IFC File|*.ifc|IFC XML File|*.ifcxml",
                FilterIndex = 0,
                Title = "Select LOIN IFC File",
                FileName = App.Settings.LastIFC,
                InitialDirectory = System.IO.Path.GetDirectoryName(App.Settings.LastIFC)
            };
            if (dlg.ShowDialog() != true)
                return;

            var path = dlg.FileName;
            App.Settings.LastIFC = path;
            OpenFile(path);
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            var startFile = (Application.Current as App).StartupFile;
            if (!string.IsNullOrWhiteSpace(startFile))
                OpenFile(startFile);
        }

        private void OpenFile(string path)
        {
            _model = LOIN.Model.Open(path);

            ContextSelector = new ContextSelector(_model);

            if (!_model.Requirements.Any())
                MessageBox.Show(this, "This file doesn't contain any requirements.", "Not a LOIN", MessageBoxButton.OK, MessageBoxImage.Warning);

            DataContext = _model;

            // breakedown structure
            BreakedownItems = _model.BreakdownStructure.Where(bs => bs.Parent == null)
                .Select(i => new BreakedownItemView(i, ContextSelector))
                .ToList();

            Actors = _model.Actors.Select(a => new ActorView(a, ContextSelector)).ToList();
            Milestones = _model.Milestones.Select(a => new MilestoneView(a, ContextSelector)).ToList();
            Reasons = _model.Reasons.Select(a => new ReasonView(a, ContextSelector)).ToList();

        }
    }
}
