﻿using LOIN.Context;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;

namespace LOIN.Requirements
{
    public class RequirementsSet : AbstractLoinEntity<IfcProjectLibrary>
    {
        private readonly List<IfcRelDeclares> _relations;
     
        public IEnumerable<IfcRelDeclares> Relations => _relations.AsReadOnly();

        internal RequirementsSet(IfcProjectLibrary lib, Model model, List<IfcRelDeclares> relations): base(lib, model)
        {
            _relations = relations;
        }

        internal static IEnumerable<RequirementsSet> GetRequirements(Model model)
        {
            var cache = new Dictionary<IfcProjectLibrary, List<IfcRelDeclares>>();
            foreach (var lib in model.Internal.Instances.OfType<IfcProjectLibrary>())
                cache.Add(lib, new List<IfcRelDeclares>());
            foreach (var rel in model.Internal.Instances.OfType<IfcRelDeclares>())
            {
                if (!(rel.RelatingContext is IfcProjectLibrary lib))
                    continue;
                cache[lib].Add(rel);
            }
            return cache.Select(kvp => new RequirementsSet(kvp.Key, model, kvp.Value));
        }

        // context
        public IEnumerable<Actor> Actors => Model.Actors.Where(a => a.IsContextFor(this));
        public IEnumerable<BreakdownItem> BreakedownItems => Model.BreakdownStructure.Where(a => a.IsContextFor(this));
        public IEnumerable<Milestone> Milestones => Model.Milestones.Where(a => a.IsContextFor(this));
        public IEnumerable<Reason> Reasons => Model.Reasons.Where(a => a.IsContextFor(this));

        // requirements
        public IEnumerable<IfcPropertySetTemplate> RequirementSets => _relations.SelectMany(r => r.RelatedDefinitions.OfType<IfcPropertySetTemplate>());
        public IEnumerable<IfcPropertyTemplate> Requirements => _relations.SelectMany(r => r.RelatedDefinitions.OfType<IfcPropertyTemplate>())
            .Union(RequirementSets.SelectMany(r => r.HasPropertyTemplates));

        public void Remove(IfcPropertyTemplateDefinition template)
        {
            foreach (var rel in _relations)
                rel.RelatedDefinitions.Remove(template);
        }

        public void Add(IfcPropertyTemplateDefinition template)
        {
            if (!_relations.Any())
            {
                var rel = Model.Internal.Instances.New<IfcRelDeclares>(r => r.RelatingContext = Entity);
                _relations.Add(rel);
            }

            var relation = _relations.FirstOrDefault();
            relation.RelatedDefinitions.Add(template);
        }
    }
}
