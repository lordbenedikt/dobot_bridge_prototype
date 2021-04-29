﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace LINQtoXML
{
    class ExampleOfXML
    {
        public void test() {
            string myXML = @"
            < root >
              < DobotType >
                < item_0 > Magician </ item_0 >
              </ DobotType >
              < row_StudioVersion >
                < item_0 > Ver - 194 </ item_0 >
              </ row_StudioVersion >
              < row0 >
                < item_0 > 1 </ item_0 >
                < item_1 />
                < item_2 > 96.4091 </ item_2 >
                < item_3 > -226.2211 </ item_3 >
                < item_4 > 73.5484 </ item_4 >
                < item_5 > -66.9176 </ item_5 >
                < item_10 > 0.0 </ item_10 >
                < item_12 > 0 </ item_12 >
              </ row0 >
              < row1 >
                < item_0 > 1 </ item_0 >
                < item_1 />
                < item_2 > 94.6241 </ item_2 >
                < item_3 > 176.5384 </ item_3 >
                < item_4 > 154.077 </ item_4 >
                < item_5 > 61.8088 </ item_5 >
                < item_10 > 0.0 </ item_10 >
                < item_12 > 0 </ item_12 >
              </ row1 >
            </ root > ";

            XDocument xdoc = new XDocument();
            xdoc = XDocument.Parse(myXML);

            var result = xdoc.Element("row1").Descendants();

            foreach (XElement item in result)
            {
                Console.WriteLine("Department Name - " + item.Value);
            }

            Console.WriteLine("\nPress any key to continue.");
            Console.ReadKey();
        }
    }
}
