<h1>FindMyClient</h1>

Revision history
# Version 1.0.0
Basic complete functionality of the application
# Version 1.0.1
<li>Added version numbering to ColdCall.ImportByDate Version column, visible from Admin mode in Global Settings
<li>Added record load status line, showing record load time progress while loading and after loading is completed it shows total time of the loading process along with total records in result

<h2>Optimizations</h2>

<li>Optimized record loading process for places withing x kilometers from the given location, now it takes up to 5 seconds for 2,500,000 records in the neighborhood of selected place
<li>Optimized indices for Phone, Email and Website. Querying after selecting these flags is now down to miliseconds.
<li>Optimized searching for company names, using trigram index and advanced filtering criteria</li>

