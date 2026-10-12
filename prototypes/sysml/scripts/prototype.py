"""Reproducible, standard-library-only Domain and fixture authoring. No replacement exporter.

Native exports are always rendered by ThinkComposer's output generate command.
"""
from __future__ import annotations
import argparse, collections, copy, gzip, hashlib, json, pathlib, re, shutil, subprocess, sys, uuid, zipfile
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
REPO = ROOT.parents[1]
NS = {'xmi': 'http://www.omg.org/spec/XMI/20131001', 'uml': 'http://www.omg.org/spec/UML/20131001',
      'sysml': 'http://www.omg.org/spec/SysML/20181001/SysML', 'Profile': 'http://www.magicdraw.com/schemas/Profile.xmi',
      'Concept_Modeling_Profile': 'http://www.magicdraw.com/schemas/Concept_Modeling_Profile.xmi'}
XI = '{' + NS['xmi'] + '}'
VERSION = '0.1.0'
DEPTH = 12
NAMESPACE = uuid.UUID('de0f986a-f733-446b-9bed-c4d2ba598d80')
META = ['SourceId', 'XmlTag', 'UmlType', 'HasName', 'SourceOrder', 'SourcePath', 'SourceType',
        'ExportStatus', 'Lower', 'Upper', 'Direction', 'Expression', 'ExpressionLanguage', 'UnitUri',
        'QualifiedName', 'TextKeyword', 'KerMLKeyword', 'TypeName', 'Specializes', 'ReferenceUri',
        'Normative', 'Informative', 'Deprecated', 'Extensible', 'ImplementationFormat', 'RequirementId',
        'RequirementText', 'NotationStatus', 'TextContent', 'Introduced', 'DeprecatedVersion', 'Observes', 'StereotypeName',
        'ImplementationXml','ImplementationJson','SimpleRequirement','SemanticRequirement','IsArray','ArraySize','ResourceIRI','VersionIRI']
MT_STEREOTYPES={'normative':'MTConnectNormative','informative':'MTConnectInformative','deprecated':'MTConnectDeprecated',
                'extensible':'MTConnectExtensible','observes':'MTConnectObserves','ImplementationFormat':'ImplementationFormat',
                'MTConnect_Requirement':'MTConnectRequirement','valueType':'MTConnectValueType','isArray':'MTConnectArray','organizer':'MTConnectOrganizer'}
KINDS = {
 'Organization': ['Model', 'Package', 'PackageImport', 'ElementImport', 'Profile', 'Stereotype', 'ExternalReference', 'Reference'],
 'Structure': ['Block', 'Class', 'DataType', 'PrimitiveType', 'ValueType', 'Enumeration', 'EnumerationLiteral', 'Property',
               'PartProperty', 'ReferenceProperty', 'ValueProperty', 'InstanceSpecification', 'Slot', 'InstanceValue',
               'PartDefinition', 'PartUsage', 'AttributeDefinition', 'AttributeUsage', 'ItemDefinition', 'ItemUsage',
               'LiteralString', 'LiteralInteger', 'LiteralReal', 'LiteralBoolean', 'LiteralUnlimitedNatural', 'ElementValue'],
 'Interfaces': ['Port', 'Interface', 'InterfaceRealization', 'Connector', 'ConnectorEnd', 'ItemFlow', 'PortDefinition',
                'PortUsage', 'ConnectionDefinition', 'ConnectionUsage', 'InterfaceDefinition', 'InterfaceUsage'],
 'Requirements': ['Requirement', 'RequirementDefinition', 'RequirementUsage', 'TestCase', 'VerificationCase', 'Rationale'],
 'Behavior': ['Activity', 'Action', 'CallBehaviorAction', 'OpaqueAction', 'InputPin', 'OutputPin', 'ActivityParameterNode',
              'CentralBufferNode', 'ObjectNode', 'InitialNode', 'ActivityFinalNode', 'FlowFinalNode', 'DecisionNode',
              'MergeNode', 'ForkNode', 'JoinNode', 'StateMachine', 'Region', 'State', 'Pseudostate', 'FinalState',
              'Transition', 'Trigger', 'Signal', 'SignalEvent', 'TimeEvent', 'Interaction', 'Lifeline', 'Message',
              'MessageOccurrenceSpecification', 'StateInvariant', 'ExecutionOccurrenceSpecification', 'CombinedFragment',
              'InteractionOperand', 'Actor', 'UseCase', 'ActionDefinition', 'ActionUsage', 'StateDefinition', 'StateUsage'],
 'Constraints': ['Constraint', 'ConstraintBlock', 'Parameter', 'OpaqueExpression', 'Expression', 'Quantity', 'Unit',
                 'ConstraintDefinition', 'ConstraintUsage', 'CalculationDefinition', 'CalculationUsage', 'AnalysisCase'],
 'Extensions': ['Comment', 'MetadataDefinition', 'MetadataUsage', 'StereotypeApplication', 'View', 'Viewpoint', 'Citation', 'UnsupportedElement'],
}
RELATIONS = ['Typing', 'Generalization', 'Dependency', 'Association', 'Realization', 'Satisfy', 'Verify', 'Refine',
             'DeriveRequirement', 'Allocate', 'ControlFlow', 'ObjectFlow', 'Binding', 'Connection', 'Redefinition',
             'Subsetting', 'Import', 'Observe', 'Trace']
UML = {v for vs in KINDS.values() for v in vs} | set(RELATIONS)
TEXT_KINDS = {'Package': ('package', 'package'), 'PartDefinition': ('part def', 'class'), 'PartUsage': ('part', 'feature'),
              'AttributeDefinition': ('attribute def', 'datatype'), 'AttributeUsage': ('attribute', 'feature'),
              'ItemDefinition': ('item def', 'class'), 'ItemUsage': ('item', 'feature'),
              'PortDefinition': ('port def', ''), 'PortUsage': ('port', ''), 'ActionDefinition': ('action def', ''),
              'ActionUsage': ('action', ''), 'StateDefinition': ('state def', ''), 'StateUsage': ('state', ''),
              'RequirementDefinition': ('requirement def', ''), 'RequirementUsage': ('requirement', ''),
              'ConstraintDefinition': ('constraint def', ''), 'ConstraintUsage': ('constraint', ''),
              'MetadataDefinition': ('metadata def', ''), 'CalculationDefinition': ('calc def', '')}

def uid(key): return str(uuid.uuid5(NAMESPACE, str(key)))
def encoded(obj): return (json.dumps(obj, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
def write_json(path, obj):
    path = pathlib.Path(path); path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(encoded(obj))
def read_json(path): return json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'))
def named(key, **extra): return dict(id=uid(key), name=key.replace('_', ' '), techName=key) | extra

def center_generated_views(composition):
    """Place the first symbol at the desktop's default initial viewport center.

    View.LastScrollOffset defaults to the center of an 11-page canvas, not (0,0).
    Preserve relative layout; only translate generated layouts near the origin.
    """
    changed=[]
    if composition is None: return changed
    for view in composition.get('views',[]):
        visuals=view.get('visuals',[])
        if not visuals or any(v.get('x',0)>2000 or v.get('y',0)>2000 for v in visuals): continue
        first=visuals[0]
        dx=1440*11/2-first.get('x',0)-first.get('width',200)/2
        dy=1070*11/2-first.get('y',0)-first.get('height',100)/2
        for visual in visuals:
            visual['x']=visual.get('x',0)+dx; visual['y']=visual.get('y',0)+dy
        changed.append(dict(viewId=view['id'],dx=dx,dy=dy,visuals=len(visuals)))
    return changed

def make_overview_visibility_explicit(composition):
    """View-local visibility must not inherit a hidden namesake's TechName control."""
    changed=[]
    if composition is None: return changed
    for view in composition.get('views',[]):
        for visual in view.get('visuals',[]):
            if 'visual' in visual: continue  # Preserve deliberate authored controls.
            visual['visual']={'display':'visible','includeInView':True}
            changed.append(dict(viewId=view['id'],ideaId=visual['ideaId']))
    return changed

def add_mtconnect_browser_views(composition):
    """Author selective drill-downs without duplicating or moving model elements.

    The five entry points and their ownership paths get populated native Views.
    Block Views expose direct semantic members; annotations remain in the tree.
    """
    nodes={n['id']:n for n in composition['ideas']}
    children=collections.defaultdict(list)
    for node in composition['ideas']: children[node.get('containerId')].append(node)
    root=next(v for v in composition['views'] if v['id']==composition['composition']['rootViewId'])
    focus={v['ideaId'] for v in root['visuals']}
    owners=set(focus)
    for identity in list(focus):
        parent=nodes[identity].get('containerId')
        while parent in nodes and parent not in owners:
            owners.add(parent); parent=nodes[parent].get('containerId')
    existing={v.get('ownerIdeaId') for v in composition['views'] if v.get('visuals')}
    added=[]
    for owner in composition['ideas']:
        if owner['id'] not in owners or owner['id'] in existing: continue
        members=children[owner['id']]
        if owner['definitionTechName']=='Block':
            members=[n for n in members if n['definitionTechName'] not in
                     ('Comment','StereotypeApplication','UnsupportedElement','Reference')]
        else: members=[n for n in members if n['id'] in owners]
        if not members: continue
        columns=min(4,len(members)); rows=(len(members)+columns-1)//columns
        width,height,gapx,gapy=190,70,25,30
        left=7920-(columns*width+(columns-1)*gapx)/2
        top=5885-(rows*height+(rows-1)*gapy)/2
        view=dict(id=uid('mtconnect-browser:'+owner['id']),ownerIdeaId=owner['id'],
                  name=owner['name']+' - selected model members',techName='Browser_'+owner['id'].replace('-',''),
                  summary='Selective prototype browser, not a conforming SysML diagram. Additional annotations and model-only content remain in the Content tree and element Details.',
                  visuals=[dict(ideaId=n['id'],x=left+(i%columns)*(width+gapx),y=top+(i//columns)*(height+gapy),
                                width=width,height=height,visual={'display':'visible','includeInView':True})
                           for i,n in enumerate(members)])
        # Put the generated View ahead of any existing empty View for this owner;
        # the native importer chooses the first one as its active drill-down.
        index=next((i for i,v in enumerate(composition['views']) if v.get('ownerIdeaId')==owner['id']),len(composition['views']))
        composition['views'].insert(index,view)
        added.append(dict(ownerId=owner['id'],viewId=view['id'],name=owner['name'],visuals=len(members)))
    return added

def package(path, domain, composition=None, template=None):
    center_generated_views(composition)
    center_generated_views(template)
    make_overview_visibility_explicit(composition)
    make_overview_visibility_explicit(template)
    parts = {'Domain.json': encoded(domain)}
    if composition is not None: parts['Composition.json'] = encoded(composition)
    if template is not None: parts['TemplateComposition.json'] = encoded(template)
    manifest = dict(format='ThinkComposer.Package', formatVersion=1, application='ThinkComposer', applicationVersion='2.1.000',
                    savedAtUtc='2026-10-08T00:00:00Z', packageKind='composition' if composition is not None else 'domain',
                    persistenceFormat='json', persistenceFormatVersion=1, legacyBinaryFallback={'present': False}, sidecars={}, warnings=[])
    manifest['authoritativeParts'] = [dict(kind='domain' if n == 'Domain.json' else 'composition' if n == 'Composition.json' else 'templateComposition',
        partUri='/' + n, format='ThinkComposer.DomainJsonInterchange' if n == 'Domain.json' else 'ThinkComposer.JsonInterchange',
        sha256=hashlib.sha256(b).hexdigest(), bytes=len(b)) for n, b in parts.items()]
    parts['manifest.json'] = encoded(manifest)
    parts['[Content_Types].xml'] = b'<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="json" ContentType="application/json"/></Types>'
    path = pathlib.Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        for n, b in parts.items():
            info = zipfile.ZipInfo(n, (2026, 10, 8, 0, 0, 0)); info.compress_type = zipfile.ZIP_DEFLATED; z.writestr(info, b)

def table(name, fields):
    return named(name, categoryTechName='Model_Data', fields=[named(name + '.' + f, name=f, techName=f,
        dataTypeTechName='TextLong' if f in ('Value', 'Expression', 'RequirementText', 'TextContent', 'SourcePath', 'ReferenceUri','ImplementationXml','ImplementationJson','SimpleRequirement','SemanticRequirement') else 'Text', order=i,
        categoryTechName='Model_Fields') for i, f in enumerate(fields)])

def detail(kind, table_name):
    return named(kind + '.' + table_name, name=table_name, techName=table_name, kind='table',
                 tableDefinitionId=uid(table_name), tableDefinitionTechName=table_name, tableDefinitionIsOwned=False,
                 appearance={'isDisplayed': False, 'isMultiRecord': table_name != 'ModelMetadata'})

def data(kind, metadata=None, attributes=None):
    result = [dict(kind='table', designatorId=uid(kind + '.ModelMetadata'), designatorTechName='ModelMetadata', records=[metadata or {}])]
    if attributes: result.append(dict(kind='table', designatorId=uid(kind + '.Attributes'), designatorTechName='Attributes', records=attributes))
    return result

def metadata(node):
    for d in node.get('details', []):
        if d.get('designatorTechName') == 'ModelMetadata': return d['records'][0]
    return {}

def template(language, text, owner='ModelDocument', scope='conceptDefinition'):
    return dict(name=language, techName=scope + '_' + owner + '_' + language, ownerScope=scope, ownerTechName=owner,
                externalLanguageTechName=language, extendsBaseTemplate=False, templateText=text)

def templates():
    """Compile bounded traversal templates, not model data or emitted output."""
    ns = read_json(ROOT / 'sources/namespaces.json')
    declarations = ' '.join('xmlns:' + p + '="' + u + '"' for p, u in ns.items() if p)
    header = '%%:FileName={{ TechName }}.xml\n%%:TemplateRole=DocumentRoot\n%%:outputValidation=XmlWellFormed\n<?xml version="1.0" encoding="UTF-8"?>\n<xmi:XMI ' + declarations + '>\n'
    xml = header + "{% for child in CompositeIdeas %}{% inject 'XmiLevel0' with child %}{% endfor %}\n</xmi:XMI>\n"
    for level in range(DEPTH):
        xml += '\n%%:SubTemplate=XmiLevel' + str(level) + '\n'
        xml += "{% assign m = This['ModelMetadata'].Records[0] %}{% if m.XmlTag != '' and m.XmlTag != null %}\n<{{ m.XmlTag }}"
        xml += "{% if m.UmlType != '' and m.UmlType != null %} xmi:type=\"{{ m.UmlType | EscapeXmlAttribute }}\"{% endif %}"
        xml += "{% if m.SourceId != '' and m.SourceId != null %} xmi:id=\"{{ m.SourceId | EscapeXmlAttribute }}\"{% else %} xmi:id=\"tc_{{ GlobalId }}\"{% endif %}"
        xml += "{% if m.HasName == 'true' %} name=\"{{ Name | EscapeXmlAttribute }}\"{% endif %}"
        xml += "{% for a in This['Attributes'].Records %} {{ a.Key }}=\"{{ a.Value | EscapeXmlAttribute }}\"{% endfor %}"
        xml += "{% if IdeaDefinitor.TechName == 'Generalization' %}{% for target in TargetIdeas %}{% assign sid = target | DetailValue: 'SourceId' %} general=\"{% if sid != '' %}{{ sid | EscapeXmlAttribute }}{% else %}tc_{{ target.GlobalId }}{% endif %}\"{% endfor %}{% endif %}>\n"
        xml += "{{ m.TextContent | EscapeXmlText }}\n"
        if level + 1 < DEPTH: xml += "{% for child in CompositeIdeas %}{% inject 'XmiLevel" + str(level+1) + "' with child %}{% endfor %}\n"
        else: xml += "{% if CompositeIdeas.Size > 0 %}<!-- PROTOTYPE_DEPTH_LIMIT: descendants not exported -->{% endif %}\n"
        xml += '</{{ m.XmlTag }}>\n{% endif %}\n'
    result = {'SysML_v1_XMI': xml}
    for language, extension, field in [('SysML_v2_Text', 'sysml', 'TextKeyword'), ('KerML_Text', 'kerml', 'KerMLKeyword')]:
        text = '%%:FileName={{ TechName }}.' + extension + '\n%%:TemplateRole=DocumentRoot\n// ThinkComposer prototype ' + VERSION + '; see companion export report.\n'
        text += "{% for child in CompositeIdeas %}{% inject 'TextLevel0' with child %}{% endfor %}\n"
        for level in range(DEPTH):
            text += '\n%%:SubTemplate=TextLevel' + str(level) + '\n'
            text += "{% assign m = This['ModelMetadata'].Records[0] %}{% if m." + field + " != '' and m." + field + " != null %}\n"
            text += "{% for link in AssociatingLinks %}{% if link.RoleDefinitor.TechName == 'Origin' and link.OwnerRelationship.IdeaDefinitor.TechName == 'Typing' %}{% for target in link.OwnerRelationship.TargetIdeas %}{% assign TypeName = target.TechName %}{% endfor %}{% endif %}{% endfor %}"
            text += "{% for link in AssociatingLinks %}{% if link.RoleDefinitor.TechName == 'Origin' and link.OwnerRelationship.IdeaDefinitor.TechName == 'Generalization' %}{% for target in link.OwnerRelationship.TargetIdeas %}{% assign Specializes = target.TechName %}{% endfor %}{% endif %}{% endfor %}"
            text += '{{ m.' + field + ' }} {{ TechName }}'
            text += "{% if m.TypeName != '' and m.TypeName != null %} : {{ m.TypeName }}{% endif %}"
            text += "{% if m.Specializes != '' and m.Specializes != null %} :> {{ m.Specializes }}{% endif %} {\n"
            text += "// source-id: {{ GlobalId }}\n"
            text += "{% if m.SourceId != '' and m.SourceId != null %}// original-xmi-id: {{ m.SourceId }}\n{% endif %}"
            text += "{% if m.TextContent != '' and m.TextContent != null %}doc /* {{ m.TextContent | Replace: '[*]/', '* /' }} */\n{% endif %}"
            if level + 1 < DEPTH: text += "{% for child in CompositeIdeas %}{% inject 'TextLevel" + str(level+1) + "' with child %}{% endfor %}\n"
            else: text += "{% if CompositeIdeas.Size > 0 %}// PROTOTYPE_DEPTH_LIMIT: descendants not exported\n{% endif %}"
            text += "}\n{% else %}{% if IdeaDefinitor.TechName == 'Typing' or IdeaDefinitor.TechName == 'Generalization' %}// Relationship emitted on its source declaration; check coverage report.\n{% else %}// UNSUPPORTED_ELEMENT {{ GlobalId }} {{ IdeaDefinitor.TechName }}\n{% endif %}{% endif %}\n"
        result[language] = text
    # Chained Detail/index/record access is not reliable in the current renderer;
    # the existing DetailValue filter reads the authoritative first table record.
    for language, text in result.items():
        fields = sorted(set(re.findall(r'\bm\.([A-Za-z]+)',text)))
        assign = ''.join("{% assign " + f + " = This | DetailValue: '" + f + "' %}" for f in fields)
        text = text.replace("{% assign m = This['ModelMetadata'].Records[0] %}",assign)
        text = re.sub(r'\bm\.([A-Za-z]+)',r'\1',text)
        text = text.replace("{% for a in This['Attributes'].Records %}","{% for d in Details %}{% if d.Designation.TechName == 'Attributes' %}{% for a in d.Records %}")
        text = text.replace('{{ a.Value | EscapeXmlAttribute }}\"{% endfor %}', '{{ a.Value | EscapeXmlAttribute }}\"{% endfor %}{% endif %}{% endfor %}')
        result[language] = text
    return result

def make_domain(key, v2=False, mt=False):
    languages = ['SysML_v2_Text', 'KerML_Text'] if v2 else ['SysML_v1_XMI']
    all_templates = templates()
    kinds = copy.deepcopy(KINDS)
    if mt: kinds['MTConnect'] = list(dict.fromkeys(list(MT_STEREOTYPES.values())+['NormativeStatement','Observation','ConceptRestriction','LiteralAnnotation','ConceptModelerAnnotation','MTConnectStereotypeApplication']))
    kinds['Organization'].insert(0, 'ModelDocument')
    domain = dict(format='ThinkComposer.DomainJsonInterchange', formatVersion=2, application='ThinkComposer',
        domain=named(key, summary='Experimental ' + VERSION + '. Not a conforming SysML tool. See prototypes/sysml/docs.',
                     representativeShape='Rectangle', isComposable=True, techSpec='prototype-version: ' + VERSION),
        externalLanguages=[named(l) for l in languages], linkRoleVariants=[named('Default')],
        conceptDefinitionClusters=[named(k) for k in kinds], relationshipDefinitionClusters=[named('Relationships')],
        tableDefinitionCategories=[named('Model_Data')], fieldDefinitionCategories=[named('Model_Fields')],
        tableDefinitions=[table('ModelMetadata', META), table('Attributes', ['Key','Value'])], conceptDefinitions=[], relationshipDefinitions=[],
        conceptDefinitionOutputTemplates=[template(l, '%%:TemplateRole=Disabled\n', scope='domainConcept', owner='') for l in languages],
        relationshipDefinitionOutputTemplates=[template(l, '%%:TemplateRole=Disabled\n', scope='domainRelationship', owner='') for l in languages], warnings=[])
    for group, items in kinds.items():
        for kind in items:
            definition = named(kind, clusterTechName=group, representativeShape='Rectangle', isComposable=True,
                summary='Prototype ' + kind + '. Language-specific semantic validation is external; notation is unverified.',
                detailDesignators=[detail(kind, 'ModelMetadata'), detail(kind, 'Attributes')],
                outputTemplates=[template(l, all_templates[l]) for l in languages] if kind == 'ModelDocument' else [])
            domain['conceptDefinitions'].append(definition)
    for kind in RELATIONS:
        domain['relationshipDefinitions'].append(named(kind, clusterTechName='Relationships', isSimple=True, hideCentralSymbolWhenSimple=True,
            summary='Explicit model relationship; no SysML inference or execution.', detailDesignators=[detail(kind, 'ModelMetadata'), detail(kind, 'Attributes')],
            roles=[named(kind+'.Origin', name='Origin', techName='Origin', roleType='Origin', set={'maxConnections':1}),
                   named(kind+'.Target', name='Target', techName='Target', roleType='Target', set={'maxConnections':1})]))
    return domain

def composition(key, domain):
    cid, vid, docid = uid(key), uid(key+'.view'), uid(key+'.document')
    document = dict(id=docid, name=key, techName='Model', definitionTechName='ModelDocument', containerId=cid,
                    details=data('ModelDocument', {'NotationStatus':'Not a SysML diagram'}))
    return dict(format='ThinkComposer.JsonInterchange', formatVersion=2, application='ThinkComposer',
        composition=dict(id=cid, name=key, techName=key, rootViewId=vid, activeViewId=vid,
                         domain={k:domain['domain'][k] for k in ('id','name','techName')}),
        ideas=[document], relationships=[], views=[dict(id=vid, name='Prototype model browser - NOT a conforming SysML diagram',
            techName='Model_Browser', ownerIdeaId=cid, visuals=[dict(ideaId=docid,x=160,y=100,width=250,height=100)])], warnings=[])

def add_node(c, kind, name, parent, m=None, attributes=None, key=None):
    identity = uid(key or c['composition']['id']+':'+name)
    meta = dict(XmlTag='packagedElement', UmlType='uml:'+kind, HasName='true', ExportStatus='partial', NotationStatus='unverified')
    if m: meta.update(m)
    node = dict(id=identity, name=name, techName=re.sub(r'[^a-zA-Z0-9_]', '_', name), definitionTechName=kind,
                containerId=parent, details=data(kind, meta, attributes), visual={'display':'hidden'})
    c['ideas'].append(node); return node

def add_relation(c,kind,origin,target,owner=None,meta=None,key=None):
    r=dict(id=uid(key or origin['id']+kind+target['id']),name=kind,techName=kind+'_'+origin['id'].replace('-',''),
           definitionTechName=kind,containerId=owner or c['ideas'][0]['id'],originIdeaIds=[origin['id']],targetIdeaIds=[target['id']],
           details=data(kind,meta or {'ExportStatus':'not-exported'}),visual={'display':'hidden'})
    c['relationships'].append(r); return r

def examples(key, d, v2, mt):
    c = composition(key + '_Survey', d); doc = c['ideas'][0]
    model = add_node(c, 'Package' if v2 else 'Model', 'Survey', doc['id'],
                     {'XmlTag':'uml:Model' if not v2 else 'packagedElement', 'TextKeyword':'package','KerMLKeyword':'package'})
    for group, kinds in KINDS.items():
        p = add_node(c, 'Package', group, model['id'], {'TextKeyword':'package','KerMLKeyword':'package'})
        selected = {'Organization':['PackageImport','ExternalReference'], 'Structure': ['PartDefinition','AttributeDefinition','PartUsage','AttributeUsage'] if v2 else ['Block','ValueType','Enumeration','Property','InstanceSpecification'],
                    'Interfaces':['PortDefinition','PortUsage'] if v2 else ['Port','Connector','ConnectorEnd'],
                    'Requirements':['RequirementDefinition','RequirementUsage'] if v2 else ['Requirement','TestCase'],
                    'Behavior':['ActionDefinition','StateDefinition','ActionUsage','StateUsage'] if v2 else ['Activity','StateMachine','Interaction','UseCase'],
                    'Constraints':['ConstraintDefinition','CalculationDefinition'] if v2 else ['ConstraintBlock','Constraint','Parameter'],
                    'Extensions':['MetadataDefinition','Citation'] if v2 else ['Comment','StereotypeApplication','Citation']}[group]
        for kind in selected:
            keywords = TEXT_KINDS.get(kind, ('',''))
            uml = {'Block':'Class','ValueType':'DataType','ConstraintBlock':'Class','Requirement':'Class','TestCase':'Operation'}.get(kind,kind)
            meta = {'TextKeyword':keywords[0], 'KerMLKeyword':keywords[1], 'UmlType':'uml:'+uml}
            if kind in ('ExternalReference','Citation'): meta.update(ReferenceUri='https://www.bipm.org/en/measurement-units', XmlTag='', UmlType='')
            if kind in ('PartUsage','AttributeUsage','PortUsage','RequirementUsage','ActionUsage','StateUsage'):
                meta['TypeName'] = kind.replace('Usage','Definition')+'Example'
            if kind in ('Constraint','ConstraintDefinition'): meta.update(Expression='value >= 0', ExpressionLanguage='illustrative; not emitted or evaluated')
            if kind == 'StereotypeApplication': meta.update(XmlTag='', UmlType='')
            add_node(c,kind,kind+'Example',p['id'],meta)
    # Unsupported examples remain editable, but are never emitted as invalid UML metaclasses/ownership.
    if not v2:
        supported={'Model','Package','Block','Class','ValueType','Enumeration','InstanceSpecification','Activity','StateMachine','Interaction','UseCase','Requirement','ConstraintBlock'}
        for n in c['ideas'][1:]:
            if n['definitionTechName'] not in supported: metadata(n)['XmlTag']=''
        bykind={n['definitionTechName']:n for n in c['ideas']}
        block=bykind['Block']; value=bykind['ValueType']; prop=bykind['Property']
        prop['containerId']=block['id']; metadata(prop).update(XmlTag='ownedAttribute',UmlType='uml:Property')
        prop['details']=data('Property',metadata(prop),[dict(Key='type',Value='tc_'+value['id'])])
        add_node(c,'EnumerationLiteral','First',bykind['Enumeration']['id'],{'XmlTag':'ownedLiteral','UmlType':'uml:EnumerationLiteral'})
        base=add_node(c,'Class','BaseElement',block['containerId'])
        add_relation(c,'Generalization',block,base,block['id'],{'XmlTag':'generalization','UmlType':'uml:Generalization','HasName':'false','ExportStatus':'mapped'})
        for kind,stereo,base_attr in [('Block','Block','base_Class'),('ValueType','ValueType','base_DataType'),('Requirement','Requirement','base_Class'),('ConstraintBlock','ConstraintBlock','base_Class')]:
            attrs=[dict(Key=base_attr,Value='tc_'+bykind[kind]['id'])]
            if kind=='Requirement': attrs += [dict(Key='id',Value='REQ-001'),dict(Key='text',Value='Preserve model identity.')]
            add_node(c,'StereotypeApplication',stereo+'Application',doc['id'],{'XmlTag':'sysml:'+stereo,'UmlType':'','HasName':'false'},attrs)
        # Explicit profile binding follows the reference namespace, without embedding proprietary profile libraries.
        application=add_node(c,'Reference','SysMLProfile',model['id'],{'XmlTag':'profileApplication','UmlType':'uml:ProfileApplication','HasName':'false'})
        add_node(c,'ExternalReference','SysMLProfileReference',application['id'],{'XmlTag':'appliedProfile','UmlType':'','HasName':'false','ReferenceUri':'http://www.omg.org/spec/SysML/20181001/SysML.xmi#SysML'},[dict(Key='href',Value='http://www.omg.org/spec/SysML/20181001/SysML.xmi#SysML')])
    else:
        byname={n['name']:n for n in c['ideas']}
        for n in list(c['ideas']):
            target=byname.get(metadata(n).get('TypeName',''))
            if target:
                metadata(n)['TypeName']=''
                add_relation(c,'Typing',n,target)
    # A visible, explicitly non-conforming browser and a real graph reference demonstrate native persistence.
    c['views'][0]['visuals']=[]
    for i,n in enumerate(c['ideas'][:8]):
        n.pop('visual',None)
        c['views'][0]['visuals'].append(dict(ideaId=n['id'],x=130+(i%4)*240,y=100+(i//4)*180,width=200,height=95))
    first, second = c['ideas'][2:4]
    c['relationships'].append(dict(id=uid(key+'.trace'), name='Example trace', techName='Example_Trace', definitionTechName='Trace',
        containerId=doc['id'], originIdeaIds=[first['id']], targetIdeaIds=[second['id']], details=data('Trace',{'ExportStatus':'not-exported'})))
    if mt:
        p=add_node(c,'Package','MTConnect',model['id'],{'TextKeyword':'package','KerMLKeyword':'package'})
        add_node(c,'MTConnectRequirement','NormativeExample',p['id'], {'Normative':'true','RequirementId':'MT-PROTOTYPE-001','RequirementText':'An example shall preserve its source identity.', 'XmlTag':'', 'UmlType':''})
        if not v2:
            target=next(n for n in c['ideas'] if n['definitionTechName']=='Block')
            add_node(c,'StereotypeApplication','NormativeApplication',doc['id'],{'XmlTag':'Profile:normative','UmlType':'','HasName':'false'},[dict(Key='base_Element',Value='tc_'+target['id'])])
    return c

def modeling_lab(key,d,v2):
    """Editable reproducer of missing behavior, interfaces and analysis semantics."""
    c=composition(key+'_ModelingLab',d); doc=c['ideas'][0]
    root=add_node(c,'Package' if v2 else 'Model','ModelingLab',doc['id'],{'TextKeyword':'package','KerMLKeyword':'package','XmlTag':'packagedElement' if v2 else 'uml:Model','UmlType':'uml:Package' if v2 else 'uml:Model'})
    def n(kind,name,parent=None,**meta):
        return add_node(c,kind,name,(parent or root)['id'],dict(XmlTag='',UmlType='',ExportStatus='blocked specialized export; inspect model and gaps',**meta))
    part=n('PartDefinition' if v2 else 'Block','Controller')
    prop=n('AttributeUsage' if v2 else 'Property','Temperature',part,Lower='1',Upper='1',UnitUri='http://qudt.org/vocab/unit/K')
    port=n('PortUsage' if v2 else 'Port','Telemetry',part,Direction='out',Lower='0',Upper='*')
    receiver=n('PartUsage' if v2 else 'Block','Receiver')
    connector=n('ConnectionUsage' if v2 else 'Connector','TelemetryPath')
    connector['details'].append(dict(kind='table',designatorId=uid(connector['definitionTechName']+'.Attributes'),designatorTechName='Attributes',records=[dict(Key='end[0]',Value=part['id']+'/'+port['id']),dict(Key='end[1]',Value=receiver['id'])]))
    add_relation(c,'Connection',port,receiver,connector['id'])
    activity=n('ActionDefinition' if v2 else 'Activity','AcquireAndPublish')
    acquire=n('ActionUsage' if v2 else 'Action','Acquire',activity); publish=n('ActionUsage' if v2 else 'Action','Publish',activity)
    parameter=n('Parameter','SampleParameter',activity,Direction='out',Lower='1',Upper='1')
    add_relation(c,'ControlFlow',acquire,publish,activity['id']); add_relation(c,'ObjectFlow',parameter,publish,activity['id'])
    machine=n('StateDefinition' if v2 else 'StateMachine','Lifecycle'); region=n('Region','MainRegion',machine)
    idle=n('StateUsage' if v2 else 'State','Idle',region); running=n('StateUsage' if v2 else 'State','Running',region)
    transition=n('Transition','StartTransition',region,Expression='startRequested',ExpressionLanguage='opaque guard')
    transition['details'].append(dict(kind='table',designatorId=uid('Transition.Attributes'),designatorTechName='Attributes',records=[dict(Key='source',Value=idle['id']),dict(Key='target',Value=running['id'])]))
    interaction=n('Interaction','PublishSequence'); sender=n('Lifeline','Sender',interaction); listener=n('Lifeline','Listener',interaction)
    for order,name in enumerate(('Request','Response')):
        n('Message',name,interaction,SourceOrder=str(order),TextContent='Sequence order retained as data; no sequence diagram renderer.')
    usecase=n('UseCase','MonitorCondition'); actor=n('Actor','Operator'); add_relation(c,'Association',actor,usecase)
    req=n('RequirementDefinition' if v2 else 'Requirement','BoundedLatency',RequirementId='REQ-LATENCY',RequirementText='Publishing shall finish within 100 ms.')
    derived=n('RequirementUsage' if v2 else 'Requirement','TelemetryLatency',RequirementId='REQ-TELEMETRY')
    test=n('VerificationCase' if v2 else 'TestCase','LatencyTest'); add_relation(c,'Satisfy',part,req); add_relation(c,'Verify',test,req); add_relation(c,'DeriveRequirement',derived,req); add_relation(c,'Allocate',activity,part)
    constraint=n('ConstraintDefinition' if v2 else 'ConstraintBlock','NonnegativeSample',Expression='value >= 0',ExpressionLanguage='prototype-integer-comparison')
    cp=n('Parameter','Value',constraint); add_relation(c,'Binding',prop,cp)
    n('AnalysisCase','LatencyAnalysis',TextContent='No evaluator or simulation backend.'); n('Quantity','TemperatureQuantity'); n('Unit','Kelvin',UnitUri='https://www.bipm.org/en/measurement-units')
    n('Viewpoint','OperatorViewpoint'); n('View','OperatorView'); n('Rationale','WhyLatencyMatters',TextContent='Illustrative explanatory text, not normative MTConnect content.')
    for i,node in enumerate([root,part,activity,machine,interaction,req,constraint]):
        node.pop('visual',None); c['views'][0]['visuals'].append(dict(ideaId=node['id'],x=150+(i%4)*245,y=260+(i//4)*170,width=220,height=100))
    return c

def build():
    for key,v2,mt in [('SysML_v1',False,False),('SysML_v2',True,False),('MTConnect_SysML_v1',False,True),('MTConnect_SysML_v2',True,True)]:
        d=make_domain(key,v2,mt); starter=composition(key+'_Starter',d); example=examples(key,d,v2,mt)
        write_json(ROOT/'sources'/ (key+'.domain.json'),d)
        package(ROOT/'domains'/(key+'.tdom'),d,template=starter)
        package(ROOT/'examples'/(key+'_Starter.tcom'),d,starter)
        package(ROOT/'examples'/(key+'_Survey.tcom'),d,example)
        package(ROOT/'examples'/(key+'_ModelingLab.tcom'),d,modeling_lab(key,d,v2))
    for lang, text in templates().items():
        path=ROOT/'templates'/(lang+'.liquid'); path.parent.mkdir(parents=True,exist_ok=True); path.write_text(text,encoding='utf-8')
    print('Built four Domains and twelve Compositions.')

def import_xmi(source, out):
    """Source-specific, loss-accounted importer. It never generates XMI output."""
    source=pathlib.Path(source); out=pathlib.Path(out); out.mkdir(parents=True,exist_ok=True)
    raw=source.read_bytes()
    if b'<!DOCTYPE' in raw.upper() or b'<!ENTITY' in raw.upper(): raise ValueError('DTD/entity declarations are unsupported; no external resolution is permitted.')
    tree=ET.fromstring(raw); digest=hashlib.sha256(raw).hexdigest()
    namespaces=read_json(ROOT/'sources/namespaces.json'); reverse={u:p for p,u in namespaces.items()}
    def qname(tag):
        if not tag.startswith('{'): return tag
        uri,local=tag[1:].split('}',1)
        return reverse.get(uri,'UNSUPPORTED_NAMESPACE')+':'+local
    d=read_json(ROOT/'sources/MTConnect_SysML_v1.domain.json'); c=composition('MTConnect_Reference',d)
    doc=c['ideas'][0]; rows=[]; source_index={}; external=collections.Counter(); counts=collections.Counter(); types=collections.Counter()
    profile_by_base=collections.defaultdict(list)
    for e in tree:
        for k,v in e.attrib.items():
            if k.startswith('base_'): profile_by_base[v].append(qname(e.tag))
    duplicates=[]; max_depth=0
    def walk(e,parent,path,depth,extension=False,exporter_metadata=False):
        nonlocal max_depth
        local=e.tag.split('}')[-1]; namespace=e.tag[1:].split('}')[0] if e.tag.startswith('{') else ''
        extension=extension or local=='Extension' and namespace in (NS['xmi'],'http://www.omg.org/XMI')
        exporter_metadata=exporter_metadata or local=='Documentation' and namespace==NS['xmi']
        sid=e.get(XI+'id',''); typ=e.get(XI+'type',''); xtype=typ.split(':')[-1]
        row=dict(sourcePath=path,sourceId=sid,sourceType=typ or qname(e.tag))
        if e.get('href'): external[e.get('href')]+=1
        structural = not extension and (namespace in ('',NS['uml'],NS['xmi']) or namespace in reverse)
        status='unsupported'; reason='proprietary extension or unknown namespace'; next_parent=parent; nextdepth=depth
        if e is tree:
            status='mapped'; reason='document envelope'
        elif exporter_metadata or parent is None:
            status='preserved-only'; reason='exporter metadata'
        elif structural:
            kind = xtype if xtype in UML else ('StereotypeApplication' if namespace not in ('',NS['uml'],NS['xmi']) else 'Reference')
            if xtype and xtype not in UML: kind='UnsupportedElement'
            if e.get('href'): kind='ExternalReference'
            if namespace==NS['Profile']: kind=MT_STEREOTYPES.get(local,'MTConnectStereotypeApplication')
            if namespace==NS['Concept_Modeling_Profile']: kind='ConceptRestriction' if local=='Restriction' else 'LiteralAnnotation' if local=='Literal_Annotation' else 'ConceptModelerAnnotation'
            if xtype=='Class' and 'sysml:Block' in profile_by_base[sid]: kind='Block'
            if xtype=='Class' and 'sysml:Requirement' in profile_by_base[sid]: kind='Requirement'
            if xtype=='DataType' and 'sysml:ValueType' in profile_by_base[sid]: kind='ValueType'
            if xtype in RELATIONS: kind='Reference' # Full source node retained; semantic edge is accounted separately below.
            status='partial' if kind in ('StereotypeApplication','MTConnectStereotypeApplication','ConceptRestriction','LiteralAnnotation','Reference','UnsupportedElement') else 'mapped'
            if namespace in (NS['Profile'],NS['Concept_Modeling_Profile']): status='partial'
            if kind=='ExternalReference': status='unresolved'
            reason='editable source structure; language semantics not enforced' if status=='mapped' else 'editable carrier; specialized semantics are not implemented'
            if kind=='UnsupportedElement': status='preserved-only'; reason='unmapped UML metaclass; retained for inspection'
            name=e.get('name') or (xtype or local)+'_'+(sid[-12:] if sid else str(len(rows)))
            attrs=[dict(Key=qname(k),Value=v) for k,v in e.attrib.items() if k not in (XI+'id',XI+'type','name')]
            meta=dict(SourceId=sid,XmlTag=qname(e.tag),UmlType=typ,HasName=str('name' in e.attrib).lower(),SourceOrder=str(len(rows)),
                      SourcePath=path,SourceType=typ or qname(e.tag),ExportStatus=status,TextContent=e.text or '',ReferenceUri=e.get('href',''))
            if kind=='UnsupportedElement': meta['XmlTag']=''
            if namespace in (NS['Profile'],NS['Concept_Modeling_Profile']):
                meta.update(StereotypeName=local,Introduced=e.get('introduced',''),DeprecatedVersion=e.get('deprecated',''),
                            Normative=str(local=='normative').lower(),Informative=str(local=='informative').lower(),
                            Extensible=str(local=='extensible').lower(),Deprecated=str(local=='deprecated' or bool(e.get('deprecated'))).lower(),
                            Observes=e.get('base_Association','') if local=='observes' else '',
                            ImplementationXml=e.get('xml',''),ImplementationJson=e.get('json',''),
                            SimpleRequirement=e.get('simple_requirement',''),SemanticRequirement=e.get('semantic_requirement',''),
                            IsArray=str(local=='isArray').lower(),ArraySize=e.get('size',''),ResourceIRI=e.get('IRI',''),VersionIRI=e.get('versionIRI',''))
            n=add_node(c,kind,name,parent,meta,attrs,key='MTConnect-source:'+ (sid or path))
            if sid:
                if sid in source_index: duplicates.append(sid)
                else: source_index[sid]=n
            next_parent=n['id']; nextdepth=depth+1; max_depth=max(max_depth,nextdepth)
            row['targetId']=n['id']; row['definition']=kind
        row.update(status=status,reason=reason); rows.append(row); counts[status]+=1; types[row['sourceType']]+=1
        seen=collections.Counter()
        for child in e:
            tag=qname(child.tag); seen[tag]+=1
            walk(child,next_parent,path+'/'+tag+'['+str(seen[tag])+']',nextdepth,extension,exporter_metadata)
    # Metadata descendants are retained in the source ledger, never interpreted as instructions.
    walk(tree,doc['id'],'/xmi:XMI[1]',0)
    # Lift unambiguous generalizations to real editable native graph edges, retaining ownership and XMI identity.
    by_native={n['id']:n for n in c['ideas']}
    converted=set()
    for n in list(c['ideas']):
        if metadata(n).get('UmlType')!='uml:Generalization': continue
        attrs=next((d['records'] for d in n['details'] if d['designatorTechName']=='Attributes'),[])
        target=source_index.get(next((a['Value'] for a in attrs if a['Key']=='general'),'')); origin=by_native.get(n['containerId'])
        if target is None or origin is None: continue
        r=add_relation(c,'Generalization',origin,target,n['containerId'],metadata(n),key=n['id'])
        r['id']=n['id']; r['name']=n['name']; r['techName']=n['techName']
        r['details']=data('Generalization',metadata(n),[a for a in attrs if a['Key']!='general'])
        converted.add(n['id'])
    c['ideas']=[n for n in c['ideas'] if n['id'] not in converted]
    for row in rows:
        if row.get('targetId') in converted: row.update(status='mapped',definition='Generalization',reason='native source/target relationship; XMI target emitted from current graph edge')
    counts=collections.Counter(row['status'] for row in rows)
    selected_names=set()
    for n in c['ideas']:
        if n['name'] in ('Device','Component','DataItem','Observation') and n['definitionTechName']=='Block' and n['name'] not in selected_names:
            i=len(selected_names); selected_names.add(n['name']); n.pop('visual',None)
            c['views'][0]['visuals'].append(dict(ideaId=n['id'],x=150+i%2*280,y=270+i//2*170,width=240,height=110))
    reference_rows=[]
    for r in c['relationships']:
        reference_rows.append(dict(sourceId=metadata(r)['SourceId'],attribute='general',target=metadata(by_native[r['targetIdeaIds'][0]]).get('SourceId',''),status='resolved',nativeRelationshipId=r['id']))
    for n in c['ideas'][1:]+c['relationships']:
        m=metadata(n)
        for detail_value in n.get('details',[]):
            if detail_value['designatorTechName']!='Attributes': continue
            for attr in detail_value['records']:
                key=attr['Key']; value=attr['Value']
                if key=='href': reference_rows.append(dict(sourceId=m['SourceId'],attribute=key,target=value,status='external-unresolved')); continue
                if key not in ('type','general','specific','client','supplier','source','target','definingFeature','classifier','association','memberEnd','annotatedElement','xmi:idref') and not key.startswith('base_'): continue
                for target in value.split():
                    ref=source_index.get(target)
                    reference_rows.append(dict(sourceId=m['SourceId'],attribute=key,target=target,status='resolved' if ref else 'unresolved'))
    add_mtconnect_browser_views(c)
    package(out/'MTConnect_Imported.tcom',d,c)
    # All source information remains recoverable even where no native semantic mapping exists.
    with gzip.open(out/'source-ledger.jsonl.gz','wt',encoding='utf-8') as f:
        for row in rows: f.write(json.dumps(row,ensure_ascii=False)+'\n')
    with gzip.open(out/'references.jsonl.gz','wt',encoding='utf-8') as f:
        for row in reference_rows: f.write(json.dumps(row,ensure_ascii=False)+'\n')
    (out/'original').mkdir(exist_ok=True); shutil.copyfile(source,out/'original'/'MTConnectSysMLModel.xml')
    report=dict(formatVersion=1,sourceFile=source.name,sourceSha256=digest,sourceBytes=len(raw),
        exporter='MagicDraw UML',exporterVersion=next((e.text for e in tree.iter() if e.tag==XI+'exporterVersion'),None),
        totalXmlElements=len(rows),accountedXmlElements=sum(counts.values()),statuses=dict(counts),types=dict(types),
        nativeConcepts=len(c['ideas']),nativeRelationships=len(c['relationships']),maxContainmentDepth=max_depth,
        templateDepthLimit=DEPTH,duplicateSourceIds=duplicates,externalReferences=dict(external),
        referenceStatuses=dict(collections.Counter(r['status'] for r in reference_rows)),
        completeSemanticImport=False,diagramImport='unsupported; original diagrams retained in unchanged source',
        note='mapped denotes editable metaclass/ownership/attributes, not SysML semantic enforcement. Local generalizations are native edges; other source relations remain editable source carriers. See gaps.')
    profiles=collections.defaultdict(lambda:dict(count=0,fields=collections.Counter()))
    for e in tree:
        if e.tag.startswith('{'+NS['Profile']+'}') or e.tag.startswith('{'+NS['Concept_Modeling_Profile']+'}'):
            p=profiles[qname(e.tag)]; p['count']+=1; p['fields'].update(qname(k) for k in e.attrib if k!=XI+'id')
    report['profileApplications']={k:dict(count=v['count'],fields=dict(v['fields'])) for k,v in profiles.items()}
    write_json(out/'import-report.json',report)
    make_reference_slices(tree,out,digest)
    print(json.dumps({k:report[k] for k in ('totalXmlElements','statuses','nativeConcepts','maxContainmentDepth','referenceStatuses')},indent=2))

def make_reference_slices(tree,out,digest):
    """Deliberately curated examples; not a v1-to-v2 migration engine."""
    chosen={}
    for e in tree.iter():
        if e.get(XI+'type')=='uml:Class' and e.get('name') in ('Device','Component','DataItem','Observation'):
            chosen.setdefault(e.get('name'),e)
    provenance=[]
    for v2 in (False,True):
        key='MTConnect_SysML_v2' if v2 else 'MTConnect_SysML_v1'
        d=read_json(ROOT/'sources'/(key+'.domain.json')); c=composition(key+'_Slice',d); doc=c['ideas'][0]
        model=add_node(c,'Package' if v2 else 'Model','MTConnectSelected',doc['id'],{'XmlTag':'packagedElement' if v2 else 'uml:Model','UmlType':'uml:Package' if v2 else 'uml:Model','TextKeyword':'package','KerMLKeyword':'package'})
        selected={}
        for name,e in chosen.items():
            comments=[ch.get('body','') for ch in e if ch.tag=='ownedComment']
            n=add_node(c,'PartDefinition' if v2 else 'Block',name,model['id'],{'SourceId':e.get(XI+'id'),'TextKeyword':'part def','KerMLKeyword':'class','UmlType':'uml:Class','TextContent':'\n'.join(comments) if v2 else '', 'ExportStatus':'partial'},key=key+':slice:'+e.get(XI+'id'))
            selected[e.get(XI+'id')]=n
            if not v2:
                add_node(c,'StereotypeApplication',name+'Block',doc['id'],{'XmlTag':'sysml:Block','UmlType':'','HasName':'false'},[dict(Key='base_Class',Value=e.get(XI+'id'))])
                for i,text in enumerate(comments): add_node(c,'Comment',name+'Documentation'+str(i),n['id'],{'XmlTag':'ownedComment','UmlType':'uml:Comment','HasName':'false'},[dict(Key='body',Value=text)])
            provenance.append(dict(language=key,name=name,sourceId=e.get(XI+'id'),targetId=n['id'],mapping='curated Class to PartDefinition' if v2 else 'Class plus Block stereotype',losses='properties and unrelated references omitted; not complete migration'))
        for e in chosen.values():
            for g in e.findall('generalization'):
                if g.get('general') in selected:
                    a=selected[e.get(XI+'id')]; b=selected[g.get('general')]
                    add_relation(c,'Generalization',a,b,a['id'],{'XmlTag':'generalization','UmlType':'uml:Generalization','HasName':'false','SourceId':g.get(XI+'id'),'ExportStatus':'mapped'})
        if not v2:
            application=add_node(c,'Reference','SysMLProfile',model['id'],{'XmlTag':'profileApplication','UmlType':'uml:ProfileApplication','HasName':'false'})
            add_node(c,'ExternalReference','ProfileURI',application['id'],{'XmlTag':'appliedProfile','UmlType':'','HasName':'false'},[dict(Key='href',Value='http://www.omg.org/spec/SysML/20181001/SysML.xmi#SysML')])
        for i,n in enumerate([model]+list(selected.values())):
            n.pop('visual',None); c['views'][0]['visuals'].append(dict(ideaId=n['id'],x=150+i%3*250,y=100+i//3*160,width=215,height=90))
        package(out/(key+'_Slice.tcom'),d,c)
    write_json(out/'slice-provenance.json',dict(sourceSha256=digest,selectionPolicy='first UML Class with each requested name in source order; alternative same-name classes are intentionally not conflated',elements=provenance))

def main():
    p=argparse.ArgumentParser(); p.add_argument('command',choices=['build','import-xmi']); p.add_argument('--input'); p.add_argument('--output-dir'); args=p.parse_args()
    if args.command=='build': build()
    elif args.command=='import-xmi':
        if not args.input or not args.output_dir: p.error('import-xmi requires --input and --output-dir')
        import_xmi(args.input,args.output_dir)

if __name__=='__main__': main()
