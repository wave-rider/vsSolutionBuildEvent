﻿/*
 * Copyright (c) 2013-2015  Denis Kuzmin (reg) <entry.reg@gmail.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using net.r_eg.vsSBE.Bridge;
using net.r_eg.vsSBE.Configuration.User;
using net.r_eg.vsSBE.Events;
using net.r_eg.vsSBE.Exceptions;
using net.r_eg.vsSBE.Extensions;
using net.r_eg.vsSBE.SBEScripts;
using net.r_eg.vsSBE.SBEScripts.Components;
using net.r_eg.vsSBE.SBEScripts.Dom;
using CEAfterEventHandler = EnvDTE._dispCommandEvents_AfterExecuteEventHandler;
using CEBeforeEventHandler = EnvDTE._dispCommandEvents_BeforeExecuteEventHandler;
using DomIcon = net.r_eg.vsSBE.SBEScripts.Dom.Icon;

namespace net.r_eg.vsSBE.UI.WForms.Logic
{
    public class Events
    {
        /// <summary>
        /// For naming actions
        /// </summary>
        public const string ACTION_PREFIX       = "Act";
        public const string ACTION_PREFIX_CLONE = "CopyOf";

        public class SBEWrap
        {
            /// <summary>
            /// Wrapped event
            /// </summary>
            public List<ISolutionEvent> evt;

            /// <summary>
            /// Specific type
            /// </summary>
            public SolutionEventType type;

            /// <param name="type"></param>
            public SBEWrap(SolutionEventType type)
            {
                this.type = type;
                update();
            }

            /// <summary>
            /// Updating list from used array data
            /// </summary>
            public void update()
            {
                if(Settings.Cfg.getEvent(type) != null) {
                    evt = new List<ISolutionEvent>(Settings.Cfg.getEvent(type));
                    return;
                }

                Log.Debug("SBEWrap: evt is null for type '{0}'", type);
                evt = new List<ISolutionEvent>();
            }
        }

        /// <summary>
        /// Provides operations with environment
        /// </summary>
        public IEnvironment Env
        {
            get;
            protected set;
        }

        /// <summary>
        /// Current SBE-event
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public SBEWrap SBE
        {
            get { return events[currentEventIndex]; }
        }

        /// <summary>
        /// Current item of SBE
        /// </summary>
        public ISolutionEvent SBEItem
        {
            get {
                if(SBE.evt.Count < 1) {
                    return null;
                }
                return SBE.evt[Math.Max(0, Math.Min(currentEventItem, SBE.evt.Count - 1))];
            }
        }

        /// <summary>
        /// Initialize the mode with end-type
        /// </summary>
        public virtual IMode DefaultMode
        {
            get { return new ModeFile(); }
        }

        /// <summary>
        /// Access to available events.
        /// </summary>
        public ISolutionEvents SlnEvents
        {
            get { return Settings.Cfg; }
        }

        /// <summary>
        /// Next unique name for action
        /// </summary>
        public string UniqueNameForAction
        {
            get {
                return genUniqueName(ACTION_PREFIX, SBE.evt);
            }
        }

        /// <summary>
        /// Predefined operations
        /// TODO:
        /// </summary>
        public List<ModeOperation> DefOperations
        {
            get { return defOperations; }
        }
        protected List<ModeOperation> defOperations = DefCommandsDTE.operations();

        /// <summary>
        /// Registered used SBE-events
        /// </summary>
        protected List<SBEWrap> events = new List<SBEWrap>();

        /// <summary>
        /// Selected event
        /// </summary>
        protected volatile int currentEventIndex = 0;

        /// <summary>
        /// Selected item of event
        /// </summary>
        protected volatile int currentEventItem = 0;

        /// <summary>
        /// List of available types of the build
        /// </summary>
        protected List<BuildType> buildType = new List<BuildType>();

        /// <summary>
        /// Used for restoring settings
        /// </summary>
        protected ISolutionEvents toRestoring;

        /// <summary>
        /// Information by existing components
        /// </summary>
        protected Dictionary<string, List<INodeInfo>> cInfo = new Dictionary<string, List<INodeInfo>>();

        /// <summary>
        /// Used loader
        /// </summary>
        protected IBootloader bootloader;

        /// Mapper of the available components
        /// </summary>
        protected IInspector inspector;

        /// <summary>
        /// Provides command events for automation clients
        /// </summary>
        protected EnvDTE.CommandEvents cmdEvents;

        /// <summary>
        /// object synch.
        /// </summary>
        private Object _lock = new Object();


        public void addEvent(SBEWrap evt)
        {
            events.Add(evt);
        }

        /// <param name="index">Selected event</param>
        /// <param name="item">Selected item of event</param>
        public void setEventIndexes(int index, int item)
        {
            currentEventIndex   = Math.Max(0, Math.Min(index, events.Count - 1));
            currentEventItem    = Math.Max(0, Math.Min(item, SBE.evt.Count - 1));
        }

        public void updateInfo(int index, string name, bool enabled)
        {
            SBE.evt[index].Name = name;
            SBE.evt[index].Enabled = enabled;
        }

        public void updateInfo(int index, SBEEvent evt)
        {
            SBE.evt[index] = evt;
        }

        /// <summary>
        /// Initialize the new Mode by type
        /// </summary>
        /// <param name="type">Available Modes</param>
        /// <returns>Mode with default values</returns>
        public IMode initMode(ModeType type)
        {
            switch(type) {
                case ModeType.Interpreter: {
                    return new ModeInterpreter();
                }
                case ModeType.File: {
                    return new ModeFile();
                }
                case ModeType.Script: {
                    return new ModeScript();
                }
                case ModeType.Targets: {
                    return new ModeTargets();
                }
                case ModeType.CSharp: {
                    return new ModeCSharp();
                }
                case ModeType.Operation: {
                    return new ModeOperation();
                }
            }
            return DefaultMode;
        }

        public bool isDefOperation(string caption)
        {
            foreach(ModeOperation operation in DefOperations) {
                if(operation.Caption == caption) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Getting the operations as array
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public string[] splitOperations(string data)
        {
            return data.Replace("\r\n", "\n").Split('\n');
        }

        /// <summary>
        /// Getting the operations as string
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public string joinOperations(string[] data)
        {
            return String.Join("\n", data);
        }

        public virtual string formatMSBuildProperty(string name, string project = null)
        {
            if(project == null) {
                return String.Format("$({0})", name);
            }
            return String.Format("$({0}:{1})", name, project);
        }

        public string genUniqueName(string prefix, List<ISolutionEvent> scope)
        {
            int id = getUniqueId(prefix, scope);
            return String.Format("{0}{1}", prefix, (id < 1) ? "" : id.ToString());
        }

        public virtual string validateName(string name)
        {
            if(String.IsNullOrEmpty(name)) {
                return UniqueNameForAction;
            }

            name = Regex.Replace(name, 
                                    @"(?:
                                            ^([^a-z]+)
                                        |
                                            ([^a-z_0-9]+)
                                        )", 
                                    delegate(Match m) { return String.Empty; }, 
                                    RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
            
            return String.IsNullOrEmpty(name)? UniqueNameForAction : name;
        }

        public void saveData()
        {
            UserConfig._.save();
            GlobalConfig._.save();
            Config._.save(); // all changes has been passed by reference
            toRestoring = SlnEvents.CloneBySerializationWithType<ISolutionEvents, SolutionEvents>(); // updating of deep copies
        }

        public void restoreData()
        {
            Settings.CfgUser.avoidRemovingFromCache();
            Config._.load(toRestoring.CloneBySerializationWithType<ISolutionEvents, SolutionEvents>());
        }

        public void fillEvents(ComboBox combo)
        {
            events.Clear();
            combo.Items.Clear();

            addEvent(new SBEWrap(SolutionEventType.Pre));
            combo.Items.Add(":: Pre-Build :: Before assembling");

            addEvent(new SBEWrap(SolutionEventType.Post));
            combo.Items.Add(":: Post-Build :: After assembling");

            addEvent(new SBEWrap(SolutionEventType.Cancel));
            combo.Items.Add(":: Cancel-Build :: by user or when occurs error");

            addEvent(new SBEWrap(SolutionEventType.CommandEvent));
            combo.Items.Add(":: CommandEvent (DTE) :: All Command Events from EnvDTE");

            addEvent(new SBEWrap(SolutionEventType.Warnings));
            combo.Items.Add(":: Warnings-Build :: Warnings during assembly processing");

            addEvent(new SBEWrap(SolutionEventType.Errors));
            combo.Items.Add(":: Errors-Build :: Errors during assembly processing");

            addEvent(new SBEWrap(SolutionEventType.OWP));
            combo.Items.Add(":: Output-Build customization :: Full control");

            addEvent(new SBEWrap(SolutionEventType.Transmitter));
            combo.Items.Add(":: Transmitter :: Transmission of the build-data to outer handler");

            addEvent(new SBEWrap(SolutionEventType.Logging));
            combo.Items.Add(":: Logging :: All processes with internal logging");

            combo.SelectedIndex = 0;
        }

        public void fillBuildTypes(ComboBox combo)
        {
            buildType.Clear();
            combo.Items.Clear();

            buildType.Add(BuildType.Common);
            combo.Items.Add("");

            buildType.Add(BuildType.Build);
            combo.Items.Add("Build");

            buildType.Add(BuildType.Rebuild);
            combo.Items.Add("Rebuild");

            buildType.Add(BuildType.Clean);
            combo.Items.Add("Clean");

            buildType.Add(BuildType.Deploy);
            combo.Items.Add("Deploy");

            buildType.Add(BuildType.Start);
            combo.Items.Add("Start Debugging");

            buildType.Add(BuildType.StartNoDebug);
            combo.Items.Add("Start Without Debugging");

            buildType.Add(BuildType.Publish);
            combo.Items.Add("Publish");

            buildType.Add(BuildType.BuildSelection);
            combo.Items.Add("Build Selection");

            buildType.Add(BuildType.RebuildSelection);
            combo.Items.Add("Rebuild Selection");

            buildType.Add(BuildType.CleanSelection);
            combo.Items.Add("Clean Selection");

            buildType.Add(BuildType.DeploySelection);
            combo.Items.Add("Deploy Selection");

            buildType.Add(BuildType.PublishSelection);
            combo.Items.Add("Publish Selection");

            buildType.Add(BuildType.BuildOnlyProject);
            combo.Items.Add("Build Project");

            buildType.Add(BuildType.RebuildOnlyProject);
            combo.Items.Add("Rebuild Project");

            buildType.Add(BuildType.CleanOnlyProject);
            combo.Items.Add("Clean Project");

            buildType.Add(BuildType.Compile);
            combo.Items.Add("Compile");

            buildType.Add(BuildType.LinkOnly);
            combo.Items.Add("Link Only");

            buildType.Add(BuildType.BuildCtx);
            combo.Items.Add("BuildCtx");

            buildType.Add(BuildType.RebuildCtx);
            combo.Items.Add("RebuildCtx");

            buildType.Add(BuildType.CleanCtx);
            combo.Items.Add("CleanCtx");

            buildType.Add(BuildType.DeployCtx);
            combo.Items.Add("DeployCtx");

            buildType.Add(BuildType.PublishCtx);
            combo.Items.Add("PublishCtx");

            combo.SelectedIndex = 0;
        }

        public int getBuildTypeIndex(BuildType type)
        {
            return buildType.IndexOf(type);
        }

        public BuildType getBuildTypeBy(int index)
        {
            Debug.Assert(index != -1);
            return buildType[index];
        }

        public void fillComponents(DataGridView grid)
        {
            grid.Rows.Clear();
            foreach(IComponent c in bootloader.Registered)
            {
                Type type = c.GetType();
                if(!Inspector.isComponent(type)) {
                    continue;
                }

                bool enabled        = c.Enabled;
                string className    = c.GetType().Name;

                Configuration.Component[] cfg = SlnEvents.Components;
                if(cfg != null && cfg.Length > 0) {
                    Configuration.Component v = cfg.Where(p => p.ClassName == className).FirstOrDefault();
                    if(v != null) {
                        enabled = v.Enabled;
                    }
                }

                cInfo[className] = new List<INodeInfo>();
                bool withoutAttr = true;

                foreach(Attribute attr in type.GetCustomAttributes(true))
                {
                    if(attr.GetType() == typeof(ComponentAttribute) || attr.GetType() == typeof(DefinitionAttribute)) {
                        withoutAttr = false;
                    }

                    if(attr.GetType() == typeof(ComponentAttribute) && ((ComponentAttribute)attr).Parent == null)
                    {
                        fillComponents((ComponentAttribute)attr, enabled, className, grid);
                    }
                    else if(attr.GetType() == typeof(DefinitionAttribute) && ((DefinitionAttribute)attr).Parent == null)
                    {
                        DefinitionAttribute def = (DefinitionAttribute)attr;
                        grid.Rows.Add(DomIcon.definition, enabled, def.Name, className, def.Description);
                    }
                    else if(((DefinitionAttribute)attr).Parent != null)
                    {
                        cInfo[className].Add(new NodeInfo((DefinitionAttribute)attr));
                    }
                    else if(((ComponentAttribute)attr).Parent != null)
                    {
                        cInfo[className].Add(new NodeInfo((ComponentAttribute)attr));
                    }
                }

                if(withoutAttr) {
                    grid.Rows.Add(DomIcon.package, enabled, String.Empty, className, String.Empty);
                }
                cInfo[className].AddRange(new List<INodeInfo>(domElemsBy(className)));
            }
            grid.Sort(grid.Columns[2], System.ComponentModel.ListSortDirection.Descending);
        }

        public void updateComponents(Configuration.Component[] components)
        {
            SlnEvents.Components = components;
            foreach(IComponent c in bootloader.Registered) {
                Configuration.Component found = components.Where(p => p.ClassName == c.GetType().Name).FirstOrDefault();
                if(found != null) {
                    c.Enabled = found.Enabled;
                }
            }
        }

        public IEnumerable<INodeInfo> infoByComponent(string className)
        {
            foreach(INodeInfo info in cInfo[className]) {
                yield return info;
            }
        }

        /// <param name="copy">Cloning the event-item at the specified index</param>
        /// <returns>added item</returns>
        public ISolutionEvent addEventItem(int copy = -1)
        {
            ISolutionEvent added;
            bool isNew = (copy >= SBE.evt.Count || copy < 0);

            switch(SBE.type)
            {
                case SolutionEventType.Pre: {
                    var evt = (isNew)? new SBEEvent() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, SBEEvent>();
                    SlnEvents.PreBuild = SlnEvents.PreBuild.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.Post: {
                    var evt = (isNew)? new SBEEvent() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, SBEEvent>();
                    SlnEvents.PostBuild = SlnEvents.PostBuild.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.Cancel: {
                    var evt = (isNew) ? new SBEEvent() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, SBEEvent>();
                    SlnEvents.CancelBuild = SlnEvents.CancelBuild.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.OWP: {
                    var evt = (isNew)? new SBEEventOWP() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, SBEEventOWP>();
                    SlnEvents.OWPBuild = SlnEvents.OWPBuild.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.Warnings: {
                    var evt = (isNew)? new SBEEventEW() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, SBEEventEW>();
                    SlnEvents.WarningsBuild = SlnEvents.WarningsBuild.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.Errors: {
                    var evt = (isNew)? new SBEEventEW() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, SBEEventEW>();
                    SlnEvents.ErrorsBuild = SlnEvents.ErrorsBuild.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.Transmitter: {
                    var evt = (isNew)? new SBETransmitter() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, SBETransmitter>();
                    SlnEvents.Transmitter = SlnEvents.Transmitter.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.CommandEvent: {
                    var evt = (isNew)? new CommandEvent() : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, CommandEvent>();
                    SlnEvents.CommandEvent = SlnEvents.CommandEvent.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                case SolutionEventType.Logging:
                {
                    var evt = (isNew)? new LoggingEvent() {
                                                Process = new EventProcess() {
                                                    Waiting = false // is better for performance
                                                }
                                            } 
                                     : SBE.evt[copy].CloneBySerializationWithType<ISolutionEvent, LoggingEvent>();

                    SlnEvents.Logging = SlnEvents.Logging.GetWithAdded(evt);
                    added = evt;
                    break;
                }
                default: {
                    throw new InvalidArgumentException("Unsupported SolutionEventType: '{0}'", SBE.type);
                }
            }
            SBE.update();
            
            // fix new data

            if(isNew) {
                added.Name = UniqueNameForAction;
                return added;
            }

            added.Caption   = String.Format("Copy of '{0}' - {1}", added.Name, added.Caption);
            added.Name      = genUniqueName(ACTION_PREFIX_CLONE + added.Name, SBE.evt);
            cacheUnlink(added.Mode);
            
            return added;
        }

        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void moveEventItem(int from, int to)
        {
            if(from == to) {
                return;
            }

            switch(SBE.type) {
                case SolutionEventType.Pre: {
                    SlnEvents.PreBuild = SlnEvents.PreBuild.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.Post: {
                    SlnEvents.PostBuild = SlnEvents.PostBuild.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.Cancel: {
                    SlnEvents.CancelBuild = SlnEvents.CancelBuild.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.OWP: {
                    SlnEvents.OWPBuild = SlnEvents.OWPBuild.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.Warnings: {
                    SlnEvents.WarningsBuild = SlnEvents.WarningsBuild.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.Errors: {
                    SlnEvents.ErrorsBuild = SlnEvents.ErrorsBuild.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.Transmitter: {
                    SlnEvents.Transmitter = SlnEvents.Transmitter.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.CommandEvent: {
                    SlnEvents.CommandEvent = SlnEvents.CommandEvent.GetWithMoved(from, to);
                    break;
                }
                case SolutionEventType.Logging: {
                    SlnEvents.Logging = SlnEvents.Logging.GetWithMoved(from, to);
                    break;
                }
            }
            SBE.update();
            setEventIndexes(currentEventIndex, to);
        }

        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void removeEventItem(int index)
        {
            cacheToRemove(SBE.evt[index].Mode);

            switch(SBE.type) {
                case SolutionEventType.Pre: {
                    SlnEvents.PreBuild = SlnEvents.PreBuild.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.Post: {
                    SlnEvents.PostBuild = SlnEvents.PostBuild.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.Cancel: {
                    SlnEvents.CancelBuild = SlnEvents.CancelBuild.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.OWP: {
                    SlnEvents.OWPBuild = SlnEvents.OWPBuild.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.Warnings: {
                    SlnEvents.WarningsBuild = SlnEvents.WarningsBuild.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.Errors: {
                    SlnEvents.ErrorsBuild = SlnEvents.ErrorsBuild.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.Transmitter: {
                    SlnEvents.Transmitter = SlnEvents.Transmitter.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.CommandEvent: {
                    SlnEvents.CommandEvent = SlnEvents.CommandEvent.GetWithRemoved(index);
                    break;
                }
                case SolutionEventType.Logging: {
                    SlnEvents.Logging = SlnEvents.Logging.GetWithRemoved(index);
                    break;
                }
            }
            SBE.update();
        }

        /// <summary>
        /// Prepare data to removing from cache.
        /// </summary>
        /// <param name="mode">Data from used mode.</param>
        public void cacheToRemove(IMode mode)
        {
            if(mode.Type == ModeType.CSharp)
            {
                IModeCSharp cfg = (IModeCSharp)mode;
                if(cfg.CacheData == null) {
                    return;
                }

                Settings.CfgUser.toRemoveFromCache(cfg.CacheData);
                cacheUnlink(mode);
            }
        }

        /// <summary>
        /// Unlink data from cache container.
        /// </summary>
        /// <param name="mode">Data from used mode.</param>
        public void cacheUnlink(IMode mode)
        {
            if(mode.Type == ModeType.CSharp) {
                ((IModeCSharp)mode).CacheData = null;
            }
        }

        /// <summary>
        /// To reset cache data.
        /// </summary>
        /// <param name="mode"></param>
        public void cacheReset(IMode mode)
        {
            if(mode.Type == ModeType.CSharp)
            {
                IModeCSharp cfg = (IModeCSharp)mode;
                if(cfg.CacheData != null) {
                    cfg.CacheData.Manager.reset();
                }
            }
        }

        /// <summary>
        /// Gets current ICommon configuration.
        /// </summary>
        /// <returns></returns>
        public ICommon getCommonCfg(ModeType type)
        {
            var data    = Settings.CfgUser.Common;
            Route route = new Route() { Event = SBE.type, Mode = type };

            if(!data.ContainsKey(route)) {
                data[route] = new Common();
            }
            return data[route];
        }

        /// <summary>
        /// Gets index from defined events
        /// </summary>
        /// <param name="type"></param>
        /// <returns>current position in list of definition</returns>
        public int getDefIndexByEventType(SolutionEventType type)
        {
            int idx = 0;
            foreach(SBEWrap evt in events)
            {
                if(evt.type == type) {
                    return idx;
                }
                ++idx;
            }
            return -1;
        }

        public void attachCommandEvents(CEBeforeEventHandler before, CEAfterEventHandler after)
        {
            cmdEvents = Env.Events.CommandEvents;
            lock(_lock) {
                cmdEvents.BeforeExecute -= before;
                cmdEvents.BeforeExecute += before;
                cmdEvents.AfterExecute  -= after;
                cmdEvents.AfterExecute  += after;
            }
        }

        public void detachCommandEvents(CEBeforeEventHandler before, CEAfterEventHandler after)
        {
            if(cmdEvents == null) {
                return;
            }
            lock(_lock) {
                cmdEvents.BeforeExecute -= before;
                cmdEvents.AfterExecute  -= after;
            }
        }

        /// <summary>
        /// Execution by user.
        /// </summary>
        public void execAction()
        {
            if(SBEItem == null) {
                Log.Info("No actions to execution. Add new, then try again.");
                return;
            }
            Actions.ICommand cmd = new Actions.Command(bootloader.Env,
                                                        new Script(bootloader),
                                                        new MSBuild.Parser(bootloader.Env, bootloader.UVariable));

            ISolutionEvent evt      = SBEItem;
            SolutionEventType type  = SBE.type;
            Log.Info("Action: execute action '{0}':'{1}' manually :: emulate '{2}' event", evt.Name, evt.Caption, type);

            try {
                bool res = cmd.exec(evt, type);
                Log.Info("Action: '{0}':'{1}' completed as - '{2}'", evt.Name, evt.Caption, res.ToString());
            }
            catch(Exception ex) {
                Log.Error("Action: '{0}':'{1}' is failed. Error: '{2}'", evt.Name, evt.Caption, ex.Message);
            }
        }

        public Events(IBootloader bootloader, IInspector inspector = null)
        {
            this.bootloader = bootloader;
            this.inspector  = inspector;
            Env             = bootloader.Env;
            toRestoring     = SlnEvents.CloneBySerializationWithType<ISolutionEvents, SolutionEvents>();
        }

        /// <summary>
        /// Generating id for present scope
        /// </summary>
        /// <param name="prefix">only for specific prefix</param>
        /// <param name="scope"></param>
        /// <returns></returns>
        protected virtual int getUniqueId(string prefix, List<ISolutionEvent> scope)
        {
            int maxId = 0;
            foreach(ISolutionEvent item in scope)
            {
                if(String.IsNullOrEmpty(item.Name)) {
                    continue;
                }

                try
                {
                    Match m = Regex.Match(item.Name, String.Format(@"^{0}(\d*)$", prefix), RegexOptions.IgnoreCase);
                    if(!m.Success) {
                        continue;
                    }
                    string num = m.Groups[1].Value;

                    maxId = Math.Max(maxId, (num.Length > 0)? Int32.Parse(num) + 1 : 1);
                }
                catch(Exception ex) {
                    Log.Debug("getUniqueId: {0} ::'{1}'", ex.ToString(), prefix);
                }
            }
            return maxId;
        }

        protected void fillComponents(ComponentAttribute attr, bool enabled, string className, DataGridView grid)
        {
            grid.Rows.Add(DomIcon.package, enabled, attr.Name, className, attr.Description);

            if(attr.Aliases == null) {
                return;
            }
            foreach(string alias in attr.Aliases)
            {
                int idx = grid.Rows.Add(DomIcon.alias, enabled, alias, className, String.Format("Alias to '{0}' Component", attr.Name));

                grid.Rows[idx].ReadOnly = true;
                grid.Rows[idx].Cells[1] = new DataGridViewCheckBoxCell() { Style = { 
                                                                               ForeColor = System.Drawing.Color.Transparent, 
                                                                               SelectionForeColor = System.Drawing.Color.Transparent }};
            }
        }

        protected IEnumerable<INodeInfo> domElemsBy(string className)
        {
            if(inspector == null) {
                Log.Debug("domElemsBy: Inspector is null");
                yield break;
            }

            List<INodeInfo> ret = new List<INodeInfo>();
            foreach(IComponent c in bootloader.Registered)
            {
                if(c.GetType().Name != className) {
                    continue;
                }

                foreach(INodeInfo info in inspector.getBy(c.GetType())) {
                    ret.Add(info);
                    ret.AddRange(domElemsBy(info.Link));
                }
            }

            // TODO:
            foreach(INodeInfo info in ret.Distinct()) {
                yield return info;
            }
        }

        protected IEnumerable<INodeInfo> domElemsBy(NodeIdent ident)
        {
            foreach(INodeInfo info in inspector.getBy(ident))
            {
                if(!String.IsNullOrEmpty(info.Name)) {
                    yield return info;
                }

                foreach(INodeInfo child in domElemsBy(info.Link)) {
                    yield return child;
                }
            }
        }
    }
}