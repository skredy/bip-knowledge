using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
namespace BipKb;

public static class TechnicalExtraction
{
    public static (List<TechnicalObject> Objects,List<KnowledgeRelation> Relations) Read(byte[] bytes,string extension,string source,SourceContext context,Evidence[] proofs)
    {
        if(extension is not ".sql" and not ".cs")return ([],[]);
        using var reader=new StreamReader(new MemoryStream(bytes),System.Text.Encoding.UTF8,true);
        var text=reader.ReadToEnd();
        var objects=new Dictionary<string,TechnicalObject>();var edges=new Dictionary<string,KnowledgeRelation>();
        string Add(string name,string kind,string? parent=null)
        {
            var id=Json.Hash(source+"|"+context.Scope+"|"+kind+"|"+name);
            var evidence=proofs.Select(e=>e.Id).ToArray();
            if(objects.TryGetValue(id,out var previous))evidence=previous.EvidenceIds.Concat(evidence).Distinct().ToArray();
            objects[id]=new(id,name,kind,evidence,parent,source,context.Project,context.Domain);return id;
        }
        void Link(string a,string b,RelationKind kind,KnowledgeOrigin origin=KnowledgeOrigin.Extracted)
        {var id=Json.Hash(a+"|"+kind+"|"+b);edges[id]=new(id,a,b,kind,proofs.Select(e=>e.Id).ToArray(),origin);}
        if(extension==".sql")
        {
            var parser=new TSql160Parser(true);var fragment=parser.Parse(new StringReader(text),out _);
            if(fragment is TSqlScript script)foreach(var statement in script.Batches.SelectMany(b=>b.Statements))
            {
                var owner=statement switch{CreateTableStatement table=>Add(table.SchemaObjectName.ToStringName(),"table"),CreateProcedureStatement proc=>Add(proc.ProcedureReference.Name.ToStringName(),"procedure"),AlterProcedureStatement proc=>Add(proc.ProcedureReference.Name.ToStringName(),"procedure"),_=>Add(source+":"+proofs.FirstOrDefault()?.RelativePath+":"+statement.StartLine,"sql-statement")};
                if(statement is CreateTableStatement t)foreach(var column in t.Definition.ColumnDefinitions){var c=Add(t.SchemaObjectName.ToStringName()+"."+column.ColumnIdentifier.Value,"column",owner);Link(c,owner,RelationKind.References);}
                var collector=new SqlReferences();statement.Accept(collector);
                foreach(var table in collector.Tables){var target=Add(table,"table");if(target!=owner)Link(owner,target,RelationKind.References);}
                foreach(var table in collector.Reads)Link(owner,Add(table,"table"),RelationKind.Reads);
                foreach(var table in collector.Writes)Link(owner,Add(table,"table"),RelationKind.Writes);
                foreach(var proc in collector.Calls)Link(owner,Add(proc,"procedure"),RelationKind.Calls);
                if(statement is ProcedureStatementBody body)foreach(var parameter in body.Parameters){var p=Add(objects[owner].Name+"."+parameter.VariableName.Value,"parameter",owner);Link(owner,p,RelationKind.References);}
            }
        }
        else if(extension==".cs")
        {
            var tree=CSharpSyntaxTree.ParseText(text);var root=tree.GetRoot();
            // Semantic binding is attempted without evaluating projects or executing MSBuild targets.
            var compilation=CSharpCompilation.Create("analysis",[tree],[MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model=compilation.GetSemanticModel(tree);
            foreach(var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var symbol=model.GetDeclaredSymbol(type);var name=symbol?.ToDisplayString()??type.Identifier.ValueText;var owner=Add(name,"type");
                if(type.BaseList!=null)foreach(var basis in type.BaseList.Types)Link(owner,Add(basis.Type.ToString(),"type-reference"),RelationKind.Implements,KnowledgeOrigin.Inferred);
                foreach(var member in type.Members)
                {
                    var declared=model.GetDeclaredSymbol(member);if(declared==null)continue;var id=Add(declared.ToDisplayString(),member is MethodDeclarationSyntax?"method":"member",owner);Link(owner,id,RelationKind.References);
                    if(member is MethodDeclarationSyntax method)Link(id,Add(method.ReturnType.ToString(),"type-reference"),RelationKind.Returns);
                    foreach(var call in member.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var target=model.GetSymbolInfo(call).Symbol;Link(id,Add(target?.ToDisplayString()??call.Expression.ToString(),target==null?"unresolved-call":"method"),RelationKind.Calls,target==null?KnowledgeOrigin.Inferred:KnowledgeOrigin.Extracted);
                    }
                }
            }
        }
        return (objects.Values.ToList(),edges.Values.ToList());
    }
    private static string ToStringName(this SchemaObjectName name)=>string.Join(".",name.Identifiers.Select(i=>i.Value));
    private sealed class SqlReferences:TSqlFragmentVisitor
    {
        public HashSet<string> Tables {get;}=[];public HashSet<string> Reads {get;}=[];public HashSet<string> Writes {get;}=[];public HashSet<string> Calls {get;}=[];
        public override void ExplicitVisit(NamedTableReference node){Tables.Add(node.SchemaObject.ToStringName());base.ExplicitVisit(node);}
        public override void ExplicitVisit(SelectStatement node){var nested=new SqlReferences();node.QueryExpression.Accept(nested);Reads.UnionWith(nested.Tables);base.ExplicitVisit(node);}
        public override void ExplicitVisit(InsertSpecification node){if(node.Target is NamedTableReference table)Writes.Add(table.SchemaObject.ToStringName());base.ExplicitVisit(node);}
        public override void ExplicitVisit(UpdateSpecification node){if(node.Target is NamedTableReference table)Writes.Add(table.SchemaObject.ToStringName());base.ExplicitVisit(node);}
        public override void ExplicitVisit(DeleteSpecification node){if(node.Target is NamedTableReference table)Writes.Add(table.SchemaObject.ToStringName());base.ExplicitVisit(node);}
        public override void ExplicitVisit(ExecutableProcedureReference node){if(node.ProcedureReference?.ProcedureReference?.Name is {} name)Calls.Add(name.ToStringName());base.ExplicitVisit(node);}
    }
}
