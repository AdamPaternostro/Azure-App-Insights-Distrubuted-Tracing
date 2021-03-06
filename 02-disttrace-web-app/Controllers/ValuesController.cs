﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace MyRESTAPI.Controllers
{


    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {



        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            // NOTE: This should be within an Application Insights Request
            string fileName = Guid.NewGuid().ToString() + ".csv";
            GenerateBlob generateBlob = new GenerateBlob();
            generateBlob.CreateBlob(fileName);
            return new string[] { fileName };
        }

        // GET api/values/5
        [HttpGet("{id}")]
        public ActionResult<string> Get(int id)
        {
            return "value";
        }

        // POST api/values
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/values/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/values/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
