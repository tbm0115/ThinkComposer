// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Thin planner facade for Domain JSON safe merge previews.
// -------------------------------------------------------------------------------------------

using Instrumind.ThinkComposer.MetaModel;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public static class DomainJsonMergePlanner
    {
        public static DomainJsonImportReport Plan(Domain TargetDomain, DomainJsonDocument Document)
        {
            return DomainJsonImporter.Preview(TargetDomain, Document);
        }
    }
}
