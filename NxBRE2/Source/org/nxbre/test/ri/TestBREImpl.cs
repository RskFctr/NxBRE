//
// NUnit v2.1 Test Class for NxBRE
//
namespace org.nxbre.test.ri
{
	using System;
	using System.Collections;
	using System.Xml.Schema;

	using net.ideaity.util;
	using net.ideaity.util.events;
	
	using org.nxbre;
	using org.nxbre.ri;
	using org.nxbre.ri.drivers;
	using org.nxbre.ri.helpers;
	using org.nxbre.util;
	using org.nxbre.rule;
	using org.nxbre.ri.rule;

	using NUnit.Framework;
	
	[TestFixture]
	public class TestBREImpl
	{
		private const int EXPECTED_CONTEXT_OPERATOR = 7;
		private const int EXPECTED_EXCEPTION = 4;
		private const int EXPECTED_WHILE = 39;
		private const int EXPECTED_GLOBALS = 13;
		private const int RULE_TESTS = 3;
		private const int LOGIC_TESTS = 21;
		
		private const string ASSERTED_HELLO_VALUE = "world";
		private static int[] ARRAY = {1,3,5,7,11};
		
		private string testFile;
		
		private IFlowEngine bre;
		
		private bool foundDynamicLog ;
		private bool foundDynamicException;
		private int logCount;
		private int exceptionCount;
		private int resultCount;
		private int whileCount;
		private int globalCount;
		private TestObject to;
		private Hashtable ht;
		private TestDataSet.Table1Row row;
		private int forEachProbe;
		private string forEachError;
		
		private class ForEachSource : IEnumerable {
			public IEnumerator GetEnumerator() {
				return new ArrayList(ARRAY).GetEnumerator();
			}
		}
		
		// These methods demonstrate the callback possibilities from rules to code.
		public object ContextlessDelegate(IBRERuleContext aBrc, Hashtable aMap, object aStep) {
			return 50;
		}
		public object ContextfullDelegate(IBRERuleContext aBrc, Hashtable aMap, object aStep) {
			object myParam = aMap["myParam"];
			
			return ((Int32)aBrc.GetObject("5i"))
							* ((myParam is Int32)?(Int32)myParam:Int32.Parse((string)myParam));
		}
		public object WhileCounter(IBRERuleContext aBrc, Hashtable aMap, object aStep) {
			whileCount++;
			// we do not care about the returned value in the rules
			return null; 
		}
		public object GlobalCounter(IBRERuleContext aBrc, Hashtable aMap, object aStep) {
			globalCount++;
			// we do not care about the returned value in the rules
			return null; 
		}
		public object GetEnumerable(IBRERuleContext aBrc, Hashtable aMap, object aStep) {
			return new ForEachSource();
		}
		public object ForEachTester(IBRERuleContext aBrc, Hashtable aMap, object aStep) {
			int contextValue = (Int32)aBrc.GetObject("ForEachParser");
			
			if (contextValue != ARRAY[forEachProbe]) {
				forEachError = "ForEachTester mismatch @"
										 + forEachProbe
										 + ": "
										 + contextValue
										 + " != "
										 + ARRAY[forEachProbe];
			}
			forEachProbe++;
			// we do not care about the returned value in the rules
			return null; 
		}
		private object GetObject(string aId) {
			return bre.RuleContext.GetObject(aId);
		}
		
		public void handleExceptionEvent(object obj, IExceptionEvent aException)
		{
			exceptionCount++;
			Console.Error.WriteLine("NxBRE ERROR " + aException.Priority + ": " + aException.Exception.ToString());
			
			// Try to catch a dynamically generated exception
			if ((aException.Priority == ExceptionEventImpl.ERROR)
					&& (aException.Exception.Message == ASSERTED_HELLO_VALUE))
				foundDynamicException = true;
		
			// Stop rule processing on fatal exceptions
			if (aException.Priority == ExceptionEventImpl.FATAL)
				bre.Stop();
		}
		
		public void handleLogEvent(object obj, ILogEvent aLog)
		{
			logCount++;
			
			if ((aLog.Priority == 3) && (aLog.Message == ASSERTED_HELLO_VALUE))
				foundDynamicLog = true;
			
			if (aLog.Priority >= 5)
				Console.Out.WriteLine("NxBRE LOG " + aLog.Priority + " MSG  : " + aLog.Message);
		}

		public virtual void handleBRERuleResult(object sender, IBRERuleResult aBRR)
		{
			resultCount++;
		}
		
		[TestFixtureSetUp]
		public void InitTest()
		{
			testFile = Parameter.GetString("unittest.inputfile");
			
			try {
				// I could have used BREFactory or BREFactoryConsole but I just wanted
				// to show what's behind the scenes and how it is possible to register
				// many handler for each event.
				bre = new BREImpl();

				// Lets register the handlers...
				logCount = 0;
				foundDynamicLog = false;
				bre.LogHandlers += new DispatchLog(handleLogEvent);
				foundDynamicException = false;
				exceptionCount = 0;
				bre.ExceptionHandlers += new DispatchException(handleExceptionEvent);
				resultCount = 0;
				bre.ResultHandlers += new DispatchRuleResult(handleBRERuleResult);
				
				// Reset the other counters
				globalCount = 0;
		
				if (bre.Init(new XBusinessRulesFileDriver(testFile)))
				{
					
					// This loads-up rules in the rule context that will be evaluated 
					bre.RuleContext.SetFactory("50i",
					    						           new BRERuleFactory(new ExecuteRuleDelegate(ContextlessDelegate)));
					
					bre.RuleContext.SetFactory("WhileCounter",
					    						           new BRERuleFactory(new ExecuteRuleDelegate(WhileCounter)));
					
					bre.RuleContext.SetFactory("GlobalCounter",
					    						           new BRERuleFactory(new ExecuteRuleDelegate(GlobalCounter)));

					bre.RuleContext.SetFactory("GetEnumerable",
					    						           new BRERuleFactory(new ExecuteRuleDelegate(GetEnumerable)));

					bre.RuleContext.SetFactory("ForEachTester",
					    						           new BRERuleFactory(new ExecuteRuleDelegate(ForEachTester)));

					
					// This alternate syntax is based on reflection thus carries performance issues
					// and prevents compile time checking.
					// It can be usefull to establish bindings on the fly.
					bre.RuleContext.SetFactory("mulBy5i",
									                 	 new BRERuleFactory(this, "ContextfullDelegate"));
					
					// This first process should only load-up some values, that we quickly reset.
					// The objective is to test that processing with no parameter works.
					bre.Process();
					bre.Reset();
					
					// We also want to ensure that cloning is harmless!
					bre = (IFlowEngine) bre.Clone();

					// Let's add test objects
					to = new TestObject(true, 10, "hello");
					bre.RuleContext.SetObject("TestObject", to);

					ht = new Hashtable();
					ht.Add("one", "first");
					ht.Add("two", "second");
					ht.Add("three", "third");
					bre.RuleContext.SetObject("TestHashtable", ht);

					TestDataSet.Table1DataTable dt = new TestDataSet.Table1DataTable();
					row = dt.NewTable1Row();
					row.col1="foo";
					row.col2=2004;
					bre.RuleContext.SetObject("TestRowSet", row);
					
					bre.RuleContext.SetObject("ReferenceAssertVersion", new Version());
					
					// this processes all global rules and the rules in the set named "Workingset"
					whileCount = 0;
					forEachProbe = 0;
					forEachError = "";
					bre.Process("WORKINGSET");
				}
			} catch (Exception e) {
				Console.Error.Write(e);
				Assert.Fail(e.Message);
			}
		}
			
		[Test]
		public void Operators() {
			Assert.AreEqual(EXPECTED_CONTEXT_OPERATOR, bre.RuleContext.OperatorMap.Count, "OperatorMap");
		}
		
		[Test]
		public void DynamicLog() {
			Assert.IsTrue(foundDynamicLog, "Dynamic Log");
		}

		[Test]
		public void Primitives() {
			Assert.AreEqual((Byte) 8, GetObject("8b"), "Byte");
			
			Assert.AreEqual(new DateTime(2003, 12, 25), GetObject("xmas2003"), "Date");
			Assert.AreEqual(new DateTime(1969, 07, 21, 02, 56, 00), GetObject("manOnMoon"), "DateTime");
			Assert.AreEqual(DateTime.Today.Add(new TimeSpan(7, 15, 30)), GetObject("wakeUpCall"), "Time");
			
			Assert.AreEqual(new DateTime(2003, 12, 25), GetObject("xmas2003_A"), "Asserted Date");
			Assert.AreEqual(new DateTime(1969, 07, 21, 02, 56, 00), GetObject("manOnMoon_A"), "Asserted DateTime");
			Assert.AreEqual(DateTime.Today.Add(new TimeSpan(7, 15, 30)), GetObject("wakeUpCall_A"), "Asserted Time");

			Assert.AreEqual(3.14m, GetObject("3.14m"), "Decimal");
			Assert.AreEqual((Single) 3.14, GetObject("3.14"), "Single");
			Assert.AreEqual(3.14d, GetObject("3.14d"), "Double");

			Assert.AreEqual((Int16) 16, GetObject("16s"), "Int16");

			Assert.AreEqual((Int32)0, GetObject("ZERO"), "Int16(0)");
			Assert.AreEqual((Int32)5, GetObject("5i"), "Int16(5)");
			Assert.AreEqual((Int32)10, GetObject("10i"), "Int16(10)");
			
			Assert.IsTrue((Boolean)GetObject("TRUE"), "Boolean(TRUE)");
			Assert.IsTrue((Boolean)GetObject("STORED_TRUE"), "Boolean(STORED_TRUE)");
			Assert.IsFalse((Boolean)GetObject("STORED_FALSE"), "Boolean(STORED_FALSE)");
		
			Assert.AreEqual(ASSERTED_HELLO_VALUE, GetObject("hello"), "String");
		}
		
		[Test]
		public void ExecutedRuleSets() {
			Assert.IsNull(GetObject("BROKENSET"), "BROKENSET");
			Assert.IsTrue((Boolean)GetObject("WORKINGSET"), "WORKINGSET");
			Assert.IsTrue((Boolean)GetObject("REFLECTION"), "REFLECTION");
			Assert.IsTrue((Boolean)GetObject("ANOTHER"), "ANOTHER");
		}
		
		[Test]
		public void Exceptions() {
			Assert.AreEqual(EXPECTED_EXCEPTION, exceptionCount, "Thrown Exceptions");
			Assert.IsTrue(GetObject("EXCP").GetType().FullName == "org.nxbre.rule.BRERuleException",
			              "Stored Exception Type");
			Assert.IsTrue(((Exception)GetObject("EXCP")).Message == "This is another exception!",
			              "Stored Exception Message");
			Assert.IsTrue(foundDynamicException, "Dynamic Exception");
			Assert.IsNull(GetObject("FATEX"), "Fatal Exception Did Not Stop!");
		}
		
		[Test]
		public void Rules() {
			for(int i=1;i<=RULE_TESTS;i++)
				Assert.IsTrue((Boolean)GetObject("RT"+i), "Rule test "+i);
		}
		
		[Test]
		public void Logic() {
			for(int i=1;i<=LOGIC_TESTS;i++)
				Assert.IsTrue((Boolean)GetObject("LT"+i), "Logic test "+i);
		}
		
		[Test]
		public void WhileLoop() {
			Assert.AreEqual(EXPECTED_WHILE, whileCount);
		}
		
		[Test]
		public void ForEach() {
			Assert.AreEqual(ARRAY.Length, forEachProbe, "ForEach not fully parsed");
			if (forEachError != "") Assert.Fail(forEachError);
		}
		
		[Test]
		public void ProcessGlobals() {
			Assert.AreEqual(EXPECTED_GLOBALS, globalCount);
		}
	
		[Test]
		public void Reflection() {
			Assert.IsFalse(to.MyField);
			Assert.AreEqual(20, to.MyProperty);
			Assert.AreEqual(ASSERTED_HELLO_VALUE, to.MyMethod());

			Assert.AreEqual(ht["two"], GetObject("Hashtable_Value"), "Item.Get");
			Assert.AreEqual("forth", ht["four"], "Item.Set");
			
			Assert.AreEqual(row["col1"], GetObject("TestRowSet_Col1"), "RowSet.Get");
			Assert.AreEqual(1969, row["col2"], "RowSet.Set");
		}
		
		[Test]
		public void ReflectionParams() {
			Assert.AreEqual(90, GetObject("TestMultiply"), "TestMultiply");
			Assert.AreEqual(14d, GetObject("TestOperate1"), "TestOperate1");
			Assert.AreEqual(25, GetObject("TestOperate2"), "TestOperate2");
		}
		
		[Test]
		public void Retract() {
			Assert.IsNull(GetObject("testAssert"), "Retract did not retract 'testAssert'.CompareTo");
		}	

	}
}
