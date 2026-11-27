window.ganttData = {
  "durataProgetto": 46,
  "camminoCritico": [
    "B",
    "D",
    "F"
  ],
  "attivita": [
    {
      "nome": "A",
      "predecessori": [],
      "durata": 12,
      "es": 0,
      "ef": 12,
      "ls": 9,
      "lf": 21
    },
    {
      "nome": "B",
      "predecessori": [],
      "durata": 4,
      "es": 0,
      "ef": 4,
      "ls": 0,
      "lf": 4
    },
    {
      "nome": "D",
      "predecessori": [
        "B"
      ],
      "durata": 12,
      "es": 4,
      "ef": 16,
      "ls": 4,
      "lf": 16
    },
    {
      "nome": "F",
      "predecessori": [
        "D"
      ],
      "durata": 30,
      "es": 16,
      "ef": 46,
      "ls": 16,
      "lf": 46
    },
    {
      "nome": "C",
      "predecessori": [
        "A",
        "B"
      ],
      "durata": 15,
      "es": 12,
      "ef": 27,
      "ls": 21,
      "lf": 36
    },
    {
      "nome": "E",
      "predecessori": [
        "C"
      ],
      "durata": 10,
      "es": 27,
      "ef": 37,
      "ls": 36,
      "lf": 46
    }
  ]
};
