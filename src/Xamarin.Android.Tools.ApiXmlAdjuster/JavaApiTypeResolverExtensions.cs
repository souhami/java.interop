using System;
using System.Collections.Generic;
using System.Linq;

namespace Xamarin.Android.Tools.ApiXmlAdjuster
{
	class JavaTypeResolutionException : Exception
	{
		public JavaTypeResolutionException (string message) : base (message)
		{
		}
	}
	
	public static class JavaApiTypeResolverExtensions
	{
		public static JavaTypeReference Parse (this JavaApi api, string name, params JavaTypeParameters [] contextTypeParameters)
		{
			var tn = JavaTypeName.Parse (name);
			return JavaTypeNameToReference (api, tn, contextTypeParameters);
		}

		static JavaTypeReference JavaTypeNameToReference (this JavaApi api, JavaTypeName tn, params JavaTypeParameters [] contextTypeParameters)
		{
			var tp = contextTypeParameters.Where (tps => tps != null).SelectMany (tps => tps.TypeParameters).FirstOrDefault (_ => _.Name == tn.DottedName);
			if (tp != null)
				return new JavaTypeReference (tp, tn.ArrayPart);
			if (tn.DottedName == JavaTypeReference.GenericWildcard.SpecialName)
				return new JavaTypeReference (tn.BoundsType, tn.GenericConstraints?.Select (gc => JavaTypeNameToReference (api, gc, contextTypeParameters)), tn.ArrayPart);
			var primitive = JavaTypeReference.GetSpecialType (tn.DottedName);
			if (primitive != null)
				return tn.ArrayPart == null && tn.GenericConstraints == null ? primitive : new JavaTypeReference (primitive, tn.ArrayPart, tn.BoundsType, tn.GenericConstraints?.Select (gc => JavaTypeNameToReference (api, gc, contextTypeParameters)));
			var type = api.FindNonGenericType (tn.FullNameNonGeneric);
			return new JavaTypeReference (type,
				tn.GenericArguments != null ? tn.GenericArguments.Select (_ => api.JavaTypeNameToReference (_, contextTypeParameters)).ToArray () : null,
				tn.ArrayPart);
		}
		
		public static JavaType FindNonGenericType (this JavaApi api, string name)
		{
			var ret = api.Packages
				.SelectMany (p => p.Types)
				.FirstOrDefault (t => name.StartsWith (t.Parent.Name, StringComparison.Ordinal) && name == t.Parent.Name + '.' + t.Name);
			if (ret == null)
				ret = ManagedType.DummyManagedPackages
				                 .SelectMany (p => p.Types)
				                 .FirstOrDefault (t => t.FullName == name);
			if (ret == null)
				throw new JavaTypeResolutionException (string.Format ("Type '{0}' was not found.", name));
			return ret;
		}
		
		public static void Resolve (this JavaApi api)
		{
			while (true) {
				bool errors = false;
				foreach (var t in api.Packages.SelectMany (p => p.Types).OfType<JavaClass> ().ToArray ())
					try { t.Resolve (); } catch (JavaTypeResolutionException ex) { Log.LogError ("Error while processing type '{0}': {1}", t, ex.Message); errors = true; t.Parent.Types.Remove (t); }
				foreach (var t in api.Packages.SelectMany (p => p.Types).OfType<JavaInterface> ().ToArray ())
					try { t.Resolve (); } catch (JavaTypeResolutionException ex) { Log.LogError ("Error while processing type '{0}': {1}", t, ex.Message); errors = true; t.Parent.Types.Remove (t); }
				if (!errors)
					break;
			}
		}
		
		static void ResolveType (this JavaType type)
		{
			if (type.TypeParameters != null)
				type.TypeParameters.Resolve (type.GetApi (), type.TypeParameters);
			foreach (var t in type.Implements)
				t.ResolvedName = type.GetApi ().Parse (t.NameGeneric, type.TypeParameters);
			
			foreach (var m in type.Members.OfType<JavaField> ().ToArray ())
				ResolveWithTryCatch (m.Resolve, m);
			foreach (var m in type.Members.OfType<JavaMethod> ().ToArray ())
				ResolveWithTryCatch (m.Resolve, m);
		}
		
		public static void Resolve (this JavaClass c)
		{
			if (c.ExtendsGeneric != null)
				c.ResolvedExtends = c.GetApi ().Parse (c.ExtendsGeneric, c.TypeParameters);
			c.ResolveType ();
			foreach (var m in c.Members.OfType<JavaConstructor> ().ToArray ())
				ResolveWithTryCatch (() => m.Resolve (), m);
		}
		
		static void ResolveWithTryCatch (Action resolve, JavaMember m)
		{
			try {
				resolve ();
			} catch (JavaTypeResolutionException ex) {
				Log.LogError ("Error while processing '{0}' in '{1}': {2}", m, m.Parent, ex.Message);
				m.Parent.Members.Remove (m);
			}
		}
		
		public static void Resolve (this JavaInterface i)
		{
			i.ResolveType ();
		}
		
		public static void Resolve (this JavaField f)
		{
			f.ResolvedType = f.GetApi ().Parse (f.TypeGeneric, f.Parent.TypeParameters);
		}
		
		static void ResolveMethodBase (this JavaMethodBase m)
		{
			if (m.TypeParameters != null)
				m.TypeParameters.Resolve (m.GetApi (), m.TypeParameters);
			foreach (var p in m.Parameters)
				p.ResolvedType = m.GetApi ().Parse (p.Type, m.Parent.TypeParameters, m.TypeParameters);
		}
		
		public static void Resolve (this JavaMethod m)
		{
			m.ResolveMethodBase ();
			m.ResolvedReturnType = m.GetApi ().Parse (m.Return, m.Parent.TypeParameters, m.TypeParameters);
		}
		
		public static void Resolve (this JavaConstructor c)
		{
			c.ResolveMethodBase ();
		}
		
		static void Resolve (this JavaTypeParameters tp, JavaApi api, params JavaTypeParameters [] additionalTypeParameters)
		{
			foreach (var t in tp.TypeParameters)
				if (t.GenericConstraints != null)
					foreach (var g in t.GenericConstraints.GenericConstraints)
						try { g.ResolvedType = api.Parse (g.Type, additionalTypeParameters); } catch (JavaTypeResolutionException ex) { Log.LogWarning ("Warning: failed to resolve generic constraint: '{0}': {1}", g.Type, ex.Message); }
		}
	}
}
