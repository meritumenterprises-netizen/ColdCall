<h1>ImportCEIDGByDate</h1>

History
# Version 1.0.0
<ul>
    <li>Basic import functionality</li>
    <ul>Importing XML data from CEIDG reports 
    <li>Importing XML data from CEIDG reports (after removing line "&lt;Lp.&gtx&lt;/Lp&gt;") into ColdCall.CompanyXml and calculating MD5</li>
    <li>Importing XML data from CEIDG reports (after removing Lp line) with new NIP into ColdCall.Companies</li>
    <li>Updating companies with NIP and company data parsed from XML if their NIP and identical MD5 does not exist in ColdCall.CompanyXml  </li>
    <li>Complete updating ColdCall.CompanyXml based on NIP, record XML, MD5 of record XML and wojewodztwoId</li>
    </ul>
</ul>

# Version 1.0.1
<ul>
    <li>Version numbering stored to ColdCall.ImportByDate Version column while running the process</li>
</ul>