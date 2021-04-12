﻿using LOIN.Context;
using LOIN.Server.Exceptions;
using LOIN.Server.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LOIN.Server.Controllers
{
    public abstract class LoinController : ControllerBase
    {
        protected LoinController(ILogger logger)
        {
            Logger = logger;
        }

        protected ILogger Logger { get; }

        protected ILoinModel Model => HttpContext.Items[Constants.RepositoryContextKey] as ILoinModel;

        protected HashSet<IContextEntity> BuildContext(string actors, string reasons, string milestones, string breakdown)
        {
            var ctx = new HashSet<IContextEntity>();

            foreach (var actor in GetContext(Model.Actors, actors))
                ctx.Add(actor);
            foreach (var reason in GetContext(Model.Reasons, reasons))
                ctx.Add(reason);
            foreach (var milestone in GetContext(Model.Milestones, milestones))
                ctx.Add(milestone);

            foreach (var item in GetContext(Model.BreakdownStructure, breakdown))
            {
                ctx.Add(item);

                if (item.Parent != null)
                {
                    foreach (var parent in item.Parents)
                    {
                        ctx.Add(parent);
                    }
                }
            }

            return ctx;
        }

        private IEnumerable<T> GetContext<T>(IEnumerable<T> source, string ids) where T : IContextEntity
        {
            if (string.IsNullOrWhiteSpace(ids))
                return Enumerable.Empty<T>();

            var labels = ParseIds(ids);
            if (labels.Count == 0)
                return Enumerable.Empty<T>();

            var result = source.Where(s => labels.Contains(s.Entity.EntityLabel)).ToList();
            if (labels.Count != result.Count)
            {
                var notFound = labels.Except(result.Select(r => r.Entity.EntityLabel)).FirstOrDefault();
                throw new EntityNotFoundException(notFound, $"Context entity {notFound} doesn't exist");
            }


            return result;
        }


        private HashSet<int> ParseIds(string value)
        {
            var ids = value.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new HashSet<int>(ids.Length);
            foreach (var id in ids)
            {
                if (int.TryParse(id, out int intId))
                    result.Add(intId);
            }
            return result;
        }

        protected IEnumerable<Requirements.RequirementsSet> ApplyContextFilter(IEnumerable<IContextEntity> context)
        {
            var contextTypes = context.GroupBy(c => c.GetType());
            var loins = Model.Requirements;
            foreach (var contextType in contextTypes)
            {
                // continuous filtering refinement
                loins = loins.Where(r => contextType.Any(c => c.IsContextFor(r)));
            }

            return loins;
        }
    }
}
