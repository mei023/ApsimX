﻿using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.Reflection;
using System.Collections;
using System.Linq;

namespace Models.Core
{
    /// <summary>
    /// Base class for all models in ApsimX.
    /// </summary>
    public class Model
    {
        // Cache the models list - this dramatically speeds up runtime!
        private List<Model> AllModels = null;

        /// <summary>
        /// Get or set the name of the model
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Get or set the parent of the model.
        /// </summary>
        [XmlIgnore]
        public Model Parent { get; set; }

        /// <summary>
        /// Get the model's full path. 
        /// Format: Simulations.SimName.PaddockName.ChildName
        /// </summary>
        public string FullPath
        {
            get
            {
                if (Parent == null)
                    return "." + Name;
                else
                    return Parent.FullPath + "." + Name;
            }
        }

        /// <summary>
        /// Return a list of all child models. A child model is any public, writtable, class property
        /// that is in the "Models" namespace. Never returns null. Can return empty list.
        /// </summary>
        public virtual Model[] Models
        {
            get
            {
                if (AllModels == null)
                {
                    AllModels = new List<Model>();
                    foreach (PropertyInfo property in ModelPropertyInfos())
                    {
                        // If a field is public and is a class 
                        object value = property.GetValue(this, null);
                        if (value != null)
                        {
                            Model localModel = value as Model;
                            if (localModel != null)
                                AllModels.Add(localModel);
                            else if (property.PropertyType.GetInterface("IList") != null)
                            {
                                Type[] arguments = property.PropertyType.GetGenericArguments();
                                if (arguments.Length > 0 && typeof(Model).IsAssignableFrom(arguments[0]))
                                {
                                    IList list = (IList)value;
                                    foreach (Model child in list)
                                        AllModels.Add(child);
                                }
                            }
                        }
                    }
                }
                return AllModels.ToArray();
            }
        }

        /// <summary>
        /// Return a model of the specified type that is in scope. Returns null if none found.
        /// </summary>
        public Model Find(Type modelType)
        {
            Model[] modelsInScope = FindAll(modelType);
            if (modelsInScope.Length >= 1)
                return modelsInScope[0];
            return null;
        }

        /// <summary>
        /// Return a model with the specified name is in scope. Returns null if none found.
        /// </summary>
        public Model Find(string modelNameToFind)
        {
            Model[] modelsInScope = FindAll();
            foreach (Model model in modelsInScope)
                if (model.Name == modelNameToFind)
                    return model;
            return null;
        }

        /// <summary>
        /// Return a list of all models in scope. If a Type is specified then only those models
        /// of that type will be returned. Never returns null. May return an empty array.
        /// </summary>
        public Model[] FindAll(Type modelType = null)
        {
            // Go looking for a zone or Simulation.
            Model m = this;
            if (!typeof(Zone).IsAssignableFrom(this.GetType()))
                m = LocateParent(typeof(Zone));

            // Get a list of children (resursively) of this zone.
            List<Model> modelsInScope = m.ModelsRecursively(modelType);

            modelsInScope.Add(m);

            // Add in the children of the zone(s) above.
            while (m.Parent != null)
            {
                m = m.Parent;
                if (m is Simulations)
                {
                    foreach (Model child in m.Models)
                    {
                        if (child.GetType() != typeof(Simulation))
                            modelsInScope.Add(child);
                    }
                    modelsInScope.Add(m);
                }
                else
                    modelsInScope.AddRange(m.Models);
                modelsInScope.Add(m);
            }

            if (modelType != null)
            {
                List<Model> modelsOfCorrectType = new List<Model>();
                foreach (Model model in modelsInScope)
                    if (modelType.IsAssignableFrom(model.GetType()))
                        modelsOfCorrectType.Add(model);
                return modelsOfCorrectType.Distinct().ToArray();
            }
            else
                return modelsInScope.Distinct().ToArray();
        }

        /// <summary>
        /// Return a model or variable using the specified NamePath. Returns null if not found.
        /// </summary>
        public object Get(string namePath)
        {
            object obj = this;
            if (namePath.StartsWith(".Simulations", StringComparison.CurrentCulture))
            {
                obj = LocateParent(typeof(Simulations));
                namePath = namePath.Remove(0, 12);
            }
            else if (namePath.StartsWith("[", StringComparison.CurrentCulture) && namePath.Contains(']'))
            {
                // namePath has a [type] at its beginning.
                int pos = namePath.IndexOf("]", StringComparison.CurrentCulture);
                string typeName = namePath.Substring(1, pos - 1);
                obj = Find(Utility.Reflection.GetTypeFromUnqualifiedName(typeName));
                namePath = namePath.Substring(pos + 1);
                if (obj == null)
                    throw new ApsimXException(FullPath, "Cannot find type: " + typeName + " while doing a get for: " + namePath);
            }

            string[] namePathBits = namePath.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            foreach (string pathBit in namePathBits)
            {
                object localObj = null;
                Model localModel = obj as Model;
                if (localModel != null)
                    localObj = localModel.LocateChild(pathBit);

                if (localObj != null)
                    obj = localObj;
                else
                {
                    object value = Utility.Reflection.GetValueOfFieldOrProperty(pathBit, obj);
                    if (value == null)
                        return null;
                    else
                        obj = value;
                }
            }
            return obj;
        }

        /// <summary>
        /// Add a model to the Models collection. Will throw if model cannot be added.
        /// </summary>
        public virtual void AddModel(Model model, bool resolveLinks)
        {
            model.Parent = this;

            // Need to find where in the object to store this model.
            bool wasAdded = false;
            foreach (PropertyInfo property in ModelPropertyInfos())
            {
                if (property.PropertyType == model.GetType())
                {
                    // Simple reference to a model.
                    property.SetValue(this, model, null);
                    wasAdded = true;
                    break;
                }
                else if (property.PropertyType.GetInterface("IList") != null)
                {
                    Type[] arguments = property.PropertyType.GetGenericArguments();
                    if (arguments.Length > 0 && arguments[0].IsAssignableFrom(model.GetType()))
                    {
                        // List<T>
                        IList value = property.GetValue(this, null) as IList;
                        if (value == null)
                        {
                            value = Activator.CreateInstance(property.PropertyType) as IList;
                            property.SetValue(this, value, null);
                        }
                        value.Add(model);
                        wasAdded = true;
                        break;
                    }
                }
            }
            if (!wasAdded)
                throw new ApsimXException(FullPath, "Cannot add model: " + model.Name + " to parent model: " + Name);

            // Invalidate the AllModels list.
            AllModels = null;
            if (resolveLinks)
                model.ResolveLinks();
        }

        /// <summary>
        /// Remove a model from the Models collection. Returns true if model was removed.
        /// </summary>
        public virtual bool RemoveModel(Model model)
        {
            // Invalidate the AllModels list.
            AllModels = null;

            // Need to find where in the object to store this model.
            foreach (PropertyInfo property in ModelPropertyInfos())
            {
                if (property.PropertyType == model.GetType())
                {
                    property.SetValue(this, null, null);
                    return true;
                }
                else if (property.PropertyType.GetInterface("IList") != null)
                {
                    Type[] arguments = property.PropertyType.GetGenericArguments();
                    if (arguments.Length > 0 && arguments[0].IsAssignableFrom(model.GetType()))
                    {
                        IList value = property.GetValue(this, null) as IList;
                        if (value != null && value.Contains(model))
                        {
                            value.Remove(model);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Recursively resolve all [Link] fields. A link must be private. This method will also
        /// go through any public members that are a model or a list of models. For each one found
        /// it will recursively call this method to resolve links in them. This is an internal
        /// method that won't normally be called by models.
        /// </summary>
        protected virtual void ResolveLinks()
        {
            // Go looking for private [Link]s
            foreach (FieldInfo field in Utility.Reflection.GetAllFields(this.GetType(),
                                                                        BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.IsDefined(typeof(Link), false))
                {
                    object linkedObject = Find(field.FieldType);
                    if (linkedObject != null)
                        field.SetValue(this, linkedObject);
                    else
                        throw new ApsimXException(FullPath, "Cannot resolve [Link] " + field.ToString() + ". Model type is " + this.GetType().FullName);
                }
            }

            foreach (Model child in Models)
            {
                // Set the childs parent property.
                child.Parent = this;

                // Tell child to resolve its links.
                child.ResolveLinks();
            }
        }

        /// <summary>
        /// Locate the parent with the specified type. Returns null if not found.
        /// </summary>
        protected Model LocateParent(Type parentType)
        {
            if (this.GetType() == parentType)
                return this;

            Model m = Parent;
            while (m != null && !parentType.IsAssignableFrom(m.GetType()))
                m = m.Parent;
            if (m != null)
                return m;
            return null;
        }

        /// <summary>
        /// Find a child model with the specified name. Returns null if not found.
        /// </summary>
        protected Model LocateChild(string nameToFind)
        {
            foreach (Model child in Models)
            {
                if (child.Name == nameToFind)
                    return child;
            }
            return null;
        }

        /// <summary>
        /// Return a list of models recursively. Never returns null. Can return empty list.
        /// </summary>
        private List<Model> ModelsRecursively(Type modelType = null)
        {
            // Get a list of children (recursively) of this zone.
            List<Model> allModels = new List<Model>();
            foreach (Model child in Models)
            {
                if (modelType == null || modelType.IsAssignableFrom(child.GetType()))
                    allModels.Add(child);
                allModels.AddRange(child.ModelsRecursively(modelType));
            }
            return allModels;
        }

        /// <summary>
        /// Return a list of all child model properties. A child model is any public, writtable, class property
        /// that is in the "Models" namespace. Never returns null. Can return empty list.
        /// </summary>
        public PropertyInfo[] ModelPropertyInfos()
        {
            List<PropertyInfo> allModelProperties = new List<PropertyInfo>();
            foreach (PropertyInfo property in this.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy))
            {
                if (property.GetType().IsClass && property.CanWrite)
                {
                    if (property.PropertyType.GetInterface("IList") != null && property.PropertyType.FullName.Contains("Models."))
                        allModelProperties.Add(property);

                    else if (property.PropertyType.IsSubclassOf(typeof(Model)))
                        allModelProperties.Add(property);
                }
            }
            return allModelProperties.ToArray();
        }

        /// <summary>
        /// Return a list of all properties (that are not references to child models). Never returns null. Can
        /// return an empty array.
        /// </summary>
        public PropertyInfo[] Properties()
        {
            List<PropertyInfo> allProperties = new List<PropertyInfo>();
            foreach (PropertyInfo property in this.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy))
            {
                if (property.CanRead && property.CanWrite)
                {
                    if ((property.PropertyType.GetInterface("IList") != null && property.PropertyType.FullName.Contains("Models."))
                        || property.PropertyType.IsSubclassOf(typeof(Model)))
                    { }
                    else
                        allProperties.Add(property);
                }
            }
            return allProperties.ToArray();
        }
    }
}