﻿using System;
using System.Xml;
using System.Reflection;
using System.Xml.Serialization;
using Models.Core;
using System.Xml.Schema;

namespace Models
{

    [ViewName("UserInterface.Views.ManagerView")]
    [PresenterName("UserInterface.Presenters.ManagerPresenter")]
    public class Manager : Model, IXmlSerializable
    {
        // Privates
        private Assembly CompiledAssembly;
        private string _Code;
        private Type ScriptType;
        private bool HasDeserialised = false;

        // Links
        [Link]
        private Zone Zone = null;

        // Publics
       // [XmlIgnore]
        public Model Model { get; set; }
        public string Code
        {
            get
            {
                return _Code;
            }
            set
            {
                _Code = value;
                RebuildScriptModel();
            }
        }
        public string[] ParameterNames { get; set; }
        public string[] ParameterValues { get; set; }

        public Zone ParentZone { get { return Zone; } }

        #region XmlSerializable methods
        /// <summary>
        /// Return our schema - needed for IXmlSerializable.
        /// </summary>
        public XmlSchema GetSchema() { return null; }

        /// <summary>
        /// Read XML from specified reader. Called during Deserialisation.
        /// </summary>
        public virtual void ReadXml(XmlReader reader)
        {
            reader.Read();
            Name = reader.ReadString();
            reader.Read();
            Code = reader.ReadString();
            reader.Read();
            CompileScript();

            // Deserialise to a model.
            XmlSerializer serial = new XmlSerializer(ScriptType);
            Model = serial.Deserialize(reader) as Model;

            // Tell reader we're done with the Manager deserialisation.
            reader.ReadEndElement();

            HasDeserialised = true;
        }

        private void CompileScript()
        {
            CompiledAssembly = Utility.Reflection.CompileTextToAssembly(Code);

            // Go look for our class name.
            ScriptType = CompiledAssembly.GetType("Models.Script");
            if (ScriptType == null)
                throw new Exception("Cannot find a public class called Script");
        }

        /// <summary>
        /// Write this point to the specified XmlWriter
        /// </summary>
        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("Name");
            writer.WriteString(Name);
            writer.WriteEndElement();
            writer.WriteStartElement("Code");
            writer.WriteString(Code);
            writer.WriteEndElement();

            // Serialise the model.
            XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
            ns.Add("", "");
            XmlSerializer serial = new XmlSerializer(Model.GetType());
            serial.Serialize(writer, Model, ns);
        }

        #endregion

        /// <summary>
        /// Initialisation
        /// </summary>
        [EventSubscribe("Initialised")]
        private void OnInitialised(object sender, EventArgs e)
        {
            //RebuildScriptModel();
        }

        /// <summary>
        /// Rebuild the script model.
        /// </summary>
        public void RebuildScriptModel()
        {
            if (HasDeserialised)
            {
                string scriptXml = null;
                if (Model != null)
                {
                    // First serialise the existing model.
                    XmlSerializer serial = new XmlSerializer(ScriptType);
                    scriptXml = Utility.Xml.Serialise(Model, true);

                    // Get rid of old script model.
                    this.RemoveModel(Model);
                    Model = null;
                }

                // Compile the script
                CompileScript();

                if (scriptXml != null)
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(scriptXml);

                    XmlSerializer serial = new XmlSerializer(ScriptType);
                    Model = serial.Deserialize(new XmlNodeReader(doc.DocumentElement)) as Model;
                }
                else
                    Model = Activator.CreateInstance(ScriptType) as Model;

                this.AddModel(Model, true);
            }
        }
    }
}